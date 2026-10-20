using SumonerWorld.Core.Randomness;
using SumonerWorld.Net.Server;
using SumonerWorld.Net.Transport;

namespace SumonerWorld.Tests.Net;

public class ServerBenchmarkTests
{
    [Fact]
    public void Measures_only_after_all_players_joined_and_the_warm_up_passed_then_reports_the_thresholds()
    {
        (MeteredTransport transport, ConnectionId client) = Connected();
        var benchmark = new ServerBenchmark(new ServerBenchmarkSettings { Players = 2, WarmupTicks = 30, MeasureTicks = 60 }, transport)
        {
            WireCounters = () => (0, 0),
        };

        Run(benchmark, transport, client, ticks: 10, connected: 1, bytesPerTick: 5000);
        Assert.Equal(ServerBenchmarkPhase.WaitingForPlayers, benchmark.Phase);

        // The tick on which the last player is in starts the warm-up; 30 more ticks warm up.
        Run(benchmark, transport, client, ticks: 31, connected: 2, bytesPerTick: 5000);
        Assert.Equal(ServerBenchmarkPhase.Measuring, benchmark.Phase);
        Run(benchmark, transport, client, ticks: 59, connected: 2, bytesPerTick: 100);
        Assert.Equal(ServerBenchmarkPhase.Measuring, benchmark.Phase);
        Run(benchmark, transport, client, ticks: 1, connected: 2, bytesPerTick: 100);
        Assert.Equal(ServerBenchmarkPhase.Done, benchmark.Phase);

        ServerBenchmarkResult result = benchmark.Result(new TransactionCounts(3, 4, 1));
        Assert.Equal(60, result.MeasuredTicks);
        Assert.Equal(2, result.MinConnected);
        Assert.Equal(2, result.Bandwidth!.Seconds);

        // Warm-up traffic (5,000 B/tick) is not counted: 30 × (100 + 44) B per second.
        Assert.Equal(30 * (100 + TrafficCounters.OverheadPerMessage) / 1024.0, result.Bandwidth.DownMean, 6);
        Assert.Empty(result.NetworkFailures);
        Assert.InRange(result.TickP99Ms, 0, 19);
        Assert.Equal(new TransactionCounts(3, 4, 1), result.Transactions);
        Assert.True(result.Passed, string.Join("; ", result.TimingFailures));
    }

    [Fact]
    public void An_unfinished_run_or_a_missing_player_fails_the_network_checks_and_files_are_written()
    {
        (MeteredTransport transport, ConnectionId client) = Connected();
        var benchmark = new ServerBenchmark(new ServerBenchmarkSettings { Players = 1, WarmupTicks = 1, MeasureTicks = 90 }, transport);
        Run(benchmark, transport, client, ticks: 2, connected: 1, bytesPerTick: 10);
        Run(benchmark, transport, client, ticks: 40, connected: 0, bytesPerTick: 10);

        ServerBenchmarkResult result = benchmark.Result(default);
        Assert.Equal(["run incomplete (Measuring, 40 of 90 ticks)", "only 0 of 1 players connected at some point"], result.NetworkFailures);
        Assert.Null(result.WireSentKiBps);
        Assert.False(result.Passed);

        string directory = Path.Combine(Path.GetTempPath(), "sw-b2-" + Guid.NewGuid().ToString("N"));
        try
        {
            benchmark.Write(directory, result);
            Assert.Contains("\"network_failures\"", File.ReadAllText(Path.Combine(directory, "summary.json")), StringComparison.Ordinal);
            string[] lines = File.ReadAllLines(Path.Combine(directory, "ticks.csv"));
            Assert.Equal(41, lines.Length);
            Assert.Equal("tick,tick_ms,ai_ms,allocated_bytes,connected", lines[0]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static (MeteredTransport Transport, ConnectionId Client) Connected()
    {
        var network = new LoopbackNetwork(new SeededRandom(1), latencySteps: 0, unreliableLossPercent: 0, bufferCount: 8192);
        LoopbackTransport serverEnd = network.CreateEndpoint(8192);
        network.CreateEndpoint(8192).Connect(serverEnd);
        var transport = new MeteredTransport(serverEnd);
        var handler = new FirstConnection();
        transport.Poll(handler);
        return (transport, handler.Connection!.Value);
    }

    private static void Run(ServerBenchmark benchmark, MeteredTransport transport, ConnectionId client, int ticks, int connected, int bytesPerTick)
    {
        byte[] payload = new byte[bytesPerTick];
        for (int i = 0; i < ticks; i++)
        {
            benchmark.BeginTick();
            transport.Send(client, Channel.Unreliable, payload.AsSpan(0, Math.Min(bytesPerTick, 1200)));
            benchmark.EndTick(connected);
        }
    }

    private sealed class FirstConnection : ITransportHandler
    {
        public ConnectionId? Connection { get; private set; }

        public void OnConnected(ConnectionId connection) => Connection ??= connection;

        public void OnDisconnected(ConnectionId connection, DisconnectReason reason)
        {
        }

        public void OnData(ConnectionId connection, Channel channel, ReadOnlySpan<byte> payload)
        {
        }
    }
}
