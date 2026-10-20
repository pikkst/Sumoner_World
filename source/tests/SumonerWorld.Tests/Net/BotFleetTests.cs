using System.Numerics;
using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Core.Movement;
using SumonerWorld.Core.Randomness;
using SumonerWorld.Core.World;
using SumonerWorld.Net.Bots;
using SumonerWorld.Net.Server;
using SumonerWorld.Net.Sessions;
using SumonerWorld.Net.Transport;
using SumonerWorld.Scenarios;
using Xunit.Abstractions;

namespace SumonerWorld.Tests.Net;

public class BotFleetTests(ITestOutputHelper output)
{
    private const string Secret = "bot-secret";

    [Fact]
    public void Sixteen_bots_at_2_percent_loss_stay_within_the_bandwidth_budget_and_pass_the_reconnect_and_duplicate_checks()
    {
        // B2 in-process (spike 2): 200 creatures, 16 bots, 2 steps (≈ 67 ms) each way plus up to a tick of polling ≈ 150 ms RTT.
        var network = new LoopbackNetwork(new SeededRandom(0xB2), latencySteps: 2, unreliableLossPercent: 2, bufferCount: 16_384);
        LoopbackTransport serverEnd = network.CreateEndpoint(4096);
        var metered = new MeteredTransport(serverEnd);
        using var world = new CreatureWorld(ShippedData.Current, new CreatureWorldSettings(), seed: 0xB2);
        var settings = new GameServerSettings();
        var server = new GameServer(world, metered, new DevTokenAuthenticator(Secret), SilentLog.Instance, settings);
        server.Populate("mossback", 200, 10);
        var fleet = new BotFleet(
            new BotFleetSettings { DevSecret = Secret, ReconnectCheckAt = 400, DuplicateCheckAt = 350, Movement = settings.Movement },
            () =>
            {
                LoopbackTransport transport = network.CreateEndpoint(2048);
                transport.Connect(serverEnd);
                return transport;
            },
            SilentLog.Instance);
        var meter = new BandwidthMeter(metered);

        // 300 ticks warm-up, 900 measured; the run ends on tick 1,199, after the last intent phase (285 of every 300).
        for (int tick = 0; tick < 1200; tick++)
        {
            network.Step();
            server.Tick();
            fleet.Step();
            if ((tick + 1) % 30 == 0)
            {
                meter.Sample();
            }

            if (tick + 1 == 300)
            {
                meter.Restart();
            }
        }

        BotFleetReport report = fleet.Report();
        BandwidthSummary bandwidth = meter.Summarize();
        output.WriteLine(report.ToString());
        output.WriteLine(bandwidth.ToString());

        Assert.True(report.Passed, report.ToString());
        Assert.Equal(16, server.ConnectedCount);
        Assert.Empty(bandwidth.Exceeded(new BandwidthBudget()));
        Assert.Equal(30, bandwidth.Seconds);
        Assert.InRange(bandwidth.DownMean, 4, 64);

        // Applied once: every distinct intent was processed once; the repeat and any resends only hit the dedupe window.
        Assert.True(report.IntentsSent > 40, $"only {report.IntentsSent} intents");
        Assert.Equal(report.IntentsSent, report.IntentsAnswered);
        Assert.Equal(report.IntentsAnswered, server.Transactions.Applied + server.Transactions.Rejected);
        Assert.True(server.Transactions.Duplicates >= 1);
        Assert.Equal(0, report.LostIntents);
    }

    [Fact]
    public void Recorded_inputs_are_the_same_for_the_same_seed_and_stay_on_the_leash()
    {
        var movement = new MovementSettings();
        BotScript first = BotScript.Record(7, 18_000, movement, leash: 40);
        BotScript again = BotScript.Record(7, 18_000, movement, leash: 40);
        BotScript other = BotScript.Record(8, 18_000, movement, leash: 40);

        Vector3 offset = Vector3.Zero;
        float farthest = 0;
        bool differs = false;
        int moving = 0;
        for (int tick = 0; tick < first.Length; tick++)
        {
            MoveInput input = first.InputAt(tick);
            Assert.Equal(input, again.InputAt(tick));
            differs |= input != other.InputAt(tick);
            moving += input == MoveInput.None ? 0 : 1;
            offset += MovementModel.Direction(input) * movement.SpeedPerTick;
            farthest = MathF.Max(farthest, offset.Length());
        }

        Assert.True(differs);
        Assert.InRange(farthest, 20, 41);
        Assert.InRange(moving, first.Length / 2, first.Length);
        Assert.Equal(first.InputAt(5), first.InputAt(first.Length + 5));
    }

    [Fact]
    public void Metered_transport_counts_payload_and_messages_per_connection_and_in_total()
    {
        var network = new LoopbackNetwork(new SeededRandom(1), latencySteps: 0, unreliableLossPercent: 0, bufferCount: 64);
        LoopbackTransport serverEnd = network.CreateEndpoint(64);
        LoopbackTransport clientEnd = network.CreateEndpoint(64);
        var metered = new MeteredTransport(serverEnd);
        ConnectionId toServer = clientEnd.Connect(serverEnd);
        var handler = new Recorder();
        metered.Poll(handler);
        ConnectionId toClient = Assert.Single(handler.Connected);

        metered.Send(toClient, Channel.Unreliable, new byte[100]);
        metered.Send(toClient, Channel.ReliableOrdered, new byte[20]);
        clientEnd.Send(toServer, Channel.Unreliable, new byte[7]);
        metered.Poll(handler);

        Assert.Equal(new TrafficCounters(120, 2, 7, 1), metered.For(toClient));
        Assert.Equal(metered.For(toClient), metered.Total);
        Assert.Equal(120 + (2 * TrafficCounters.OverheadPerMessage), metered.Total.WireBytesSent);
        Assert.Equal(default, metered.For(new ConnectionId(99)));
        Assert.Equal(7, Assert.Single(handler.Lengths));
    }

    [Fact]
    public void Bandwidth_meter_reports_per_client_seconds_and_flags_what_exceeds_the_budget()
    {
        var network = new LoopbackNetwork(new SeededRandom(1), latencySteps: 0, unreliableLossPercent: 0, bufferCount: 4096);
        LoopbackTransport serverEnd = network.CreateEndpoint(4096);
        var metered = new MeteredTransport(serverEnd);
        LoopbackTransport a = network.CreateEndpoint(4096);
        LoopbackTransport b = network.CreateEndpoint(4096);
        a.Connect(serverEnd);
        b.Connect(serverEnd);
        var handler = new Recorder();
        metered.Poll(handler);
        var meter = new BandwidthMeter(metered);

        // Second 1: 10 KiB of payload to each client (before warm-up ends). Second 2: 100 × 1 KiB to client 0, 40 × 1 KiB to client 1.
        Send(metered, handler.Connected[0], 10);
        Send(metered, handler.Connected[1], 10);
        meter.Sample();
        meter.Restart();
        Send(metered, handler.Connected[0], 100);
        Send(metered, handler.Connected[1], 40);
        meter.Sample();
        a.Poll(handler);
        b.Poll(handler);

        BandwidthSummary summary = meter.Summarize();
        double overhead = TrafficCounters.OverheadPerMessage / 1024.0;
        Assert.Equal(1, summary.Seconds);
        Assert.Equal(2, summary.ClientSeconds);
        Assert.Equal(100 * (1 + overhead), summary.DownMax, 6);
        Assert.Equal(70 * (1 + overhead), summary.DownMean, 6);
        Assert.Equal(70, summary.PayloadDownMean, 6);
        Assert.Equal(140 * (1 + overhead), summary.ServerSentMean, 6);
        Assert.Equal(["down average 73.0 KiB/s > 64 KiB/s"], summary.Exceeded(new BandwidthBudget()));
        Assert.Empty(summary.Exceeded(new BandwidthBudget { DownAverage = 80 }));
    }

    private static void Send(MeteredTransport transport, ConnectionId connection, int kibibytes)
    {
        for (int i = 0; i < kibibytes; i++)
        {
            transport.Send(connection, Channel.Unreliable, new byte[1024]);
        }
    }

    private sealed class Recorder : ITransportHandler
    {
        public List<ConnectionId> Connected { get; } = [];

        public List<int> Lengths { get; } = [];

        public void OnConnected(ConnectionId connection) => Connected.Add(connection);

        public void OnDisconnected(ConnectionId connection, DisconnectReason reason)
        {
        }

        public void OnData(ConnectionId connection, Channel channel, ReadOnlySpan<byte> payload) => Lengths.Add(payload.Length);
    }

    private sealed class SilentLog : ILog
    {
        public static readonly SilentLog Instance = new();

        public bool IsEnabled(LogLevel level) => false;

        public void Write(LogLevel level, string eventName, string message)
        {
        }
    }
}
