using System.Numerics;
using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Cognition.Lod;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Cognition.Opinions;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Movement;
using SumonerWorld.Core.Randomness;
using SumonerWorld.Core.World;
using SumonerWorld.Net.Client;
using SumonerWorld.Net.Messages;
using SumonerWorld.Net.Server;
using SumonerWorld.Net.Sessions;
using SumonerWorld.Net.Transport;
using SumonerWorld.Scenarios;

namespace SumonerWorld.Tests.Net;

public class DevToolsTests
{
    private const string Secret = "dev-secret";

    [Fact]
    public void Dev_messages_round_trip_and_the_largest_inspection_fits_one_datagram()
    {
        Assert.Equal(new DevRequest(true, 77), RoundTrip(new DevRequestCodec(), new DevRequest(true, 77)));

        StageTiming[] stages = [.. Enumerable.Range(0, ServerMetrics.StageCount).Select(i => new StageTiming(i * 10, i * 100))];
        var metrics = new ServerMetrics(9_000, 15, new StageTiming(1_250, 2_900), stages, 48, [3, 1, 0], [10, 20, 170, 0], 2, 600_000, 19_500, 210);
        ServerMetrics read = RoundTrip(new ServerMetricsCodec(), metrics);
        Assert.Equal((metrics.Tick, metrics.Ticks, metrics.TickTime, metrics.AllocatedPerTick), (read.Tick, read.Ticks, read.TickTime, read.AllocatedPerTick));
        Assert.Equal(stages, read.Stages);
        Assert.Equal([10, 20, 170, 0], read.Tiers);
        Assert.Equal((2, 600_000L, 19_500L, 210), (read.Players, read.SentBytesPerSecond, read.ClientBytesPerSecond, read.WorkingSetMiB));
        Assert.Equal(new StageTiming(20 + 30 + 40, 200 + 300 + 400), read.Ai);

        CreatureInspection largest = Largest();
        var buffer = new byte[Protocol.MaxMessageSize];
        var writer = new MessageWriter(buffer);
        new DevInspectionCodec().Write(ref writer, new DevInspection(ulong.MaxValue, largest));
        Assert.True(writer.Written <= DevInspectionCodec.MaxBytes, $"{writer.Written} bytes > {DevInspectionCodec.MaxBytes}");
        Assert.True(DevInspectionCodec.MaxBytes <= Protocol.MaxMessageSize, $"{DevInspectionCodec.MaxBytes} bytes");

        DevInspection back = RoundTrip(new DevInspectionCodec(), new DevInspection(ulong.MaxValue, largest));
        CreatureInspection got = back.Inspection!;
        Assert.Equal(largest with { Needs = got.Needs, Memories = got.Memories, Opinions = got.Opinions, Goals = got.Goals }, got);
        Assert.Equal(largest.Needs, got.Needs);
        Assert.Equal(largest.Memories, got.Memories);
        Assert.Equal(largest.Opinions, got.Opinions);
        Assert.Equal(largest.Goals, got.Goals);
        Assert.Equal(new DevInspection(5, null), RoundTrip(new DevInspectionCodec(), new DevInspection(5, null)));
    }

    [Fact]
    public void Out_of_range_dev_messages_are_malformed()
    {
        byte[] good = Encode(new DevInspection(1, Largest()));
        int tierOffset = 3 + 1 + 1 + 10 + 10 + 3 + 5;
        Assert.Equal((byte)AiLodTier.Abstract, good[tierOffset]);
        good[tierOffset] = 9;
        Assert.Throws<MalformedMessageException>(() => Read(new DevInspectionCodec(), good));

        StageTiming[] tooFew = [new StageTiming(1, 1)];
        var metrics = new ServerMetrics(1, 1, default, tooFew, 0, [0, 0, 0], [0, 0, 0, 0], 0, 0, 0, 0);
        var buffer = new byte[Protocol.MaxMessageSize];
        Assert.Throws<ArgumentException>(() =>
        {
            var writer = new MessageWriter(buffer);
            new ServerMetricsCodec().Write(ref writer, metrics);
        });
    }

    [Fact]
    public void A_dev_server_sends_metrics_and_the_inspected_mind_twice_a_second_and_again_after_a_reconnect()
    {
        using var lan = new Lan(devTools: true);
        StableId creature = lan.SpawnCreature(lan.Settings.SpawnPoint + new Vector3(4, 0, 0));
        GameClient alice = lan.Join(1001);
        lan.Run(20);
        alice.RequestDevTools(metrics: true, inspect: creature.Value);
        lan.Run(40);

        ServerMetrics metrics = alice.DevMetrics!.Value;
        Assert.Equal(GameServer.DevReportEveryTicks, metrics.Ticks);
        Assert.Equal(ServerMetrics.StageCount, metrics.Stages.Count);
        Assert.Equal(lan.World.World.Store<AiTier>().Count, metrics.Tiers.Sum());
        Assert.True(metrics.TickTime.MaxMicros >= metrics.TickTime.MeanMicros && metrics.TickTime.MeanMicros > 0);
        Assert.True(metrics.ClientBytesPerSecond > 0 && metrics.SentBytesPerSecond >= metrics.ClientBytesPerSecond);
        Assert.Equal(1, metrics.Players);

        CreatureInspection inspection = alice.Inspection!;
        Assert.Equal(creature, inspection.Id);
        Assert.InRange(lan.World.Clock.Current.Index - inspection.Tick, 0UL, (ulong)GameServer.DevReportEveryTicks + 3);
        Assert.NotEmpty(inspection.Goals);
        Assert.Equal(1, lan.Server.DevTools!.Subscribers);

        lan.TransportOf(alice).Disconnect(alice.Session.Connection);
        lan.Run(5);
        Assert.Equal(0, lan.Server.DevTools.Subscribers);
        lan.Reconnect(alice);
        lan.Run(40);
        Assert.Equal(1, lan.Server.DevTools.Subscribers);
        Assert.True(alice.Inspection!.Tick > inspection.Tick, "Inspections resumed after the reconnect.");

        alice.RequestDevTools(metrics: false, inspect: 999_999);
        lan.Run(20);
        Assert.Null(alice.Inspection);
        Assert.True(alice.InspectionGone);
        Assert.Null(alice.DevMetrics);

        alice.RequestDevTools(metrics: false, inspect: 0);
        lan.Run(5);
        Assert.Equal(0, lan.Server.DevTools.Subscribers);
    }

    [Fact]
    public void A_server_without_dev_tools_ignores_the_request_and_keeps_the_player()
    {
        using var lan = new Lan(devTools: false);
        StableId creature = lan.SpawnCreature(lan.Settings.SpawnPoint + new Vector3(4, 0, 0));
        GameClient alice = lan.Join(1001);
        lan.Run(20);
        alice.RequestDevTools(metrics: true, inspect: creature.Value);
        lan.Run(40);

        Assert.Null(alice.DevMetrics);
        Assert.Null(alice.Inspection);
        Assert.Null(lan.Server.DevTools);
        Assert.Equal(ClientSessionState.InSession, alice.Session.State);
        Assert.Contains(lan.Log.Events, entry => entry == "dev.refused");
    }

    private static CreatureInspection Largest()
    {
        var far = new MemorySubject(SubjectKind.Item, ulong.MaxValue);
        InspectedMemory[] memories = [.. Enumerable.Range(0, CreatureInspection.MaxMemories)
            .Select(i => new InspectedMemory(ushort.MaxValue, far, (sbyte)(i % 2 == 0 ? -100 : 100), ICreatureMindStore.MaxSalience, ushort.MaxValue, MemoryTags.Core | MemoryTags.Rumour | MemoryTags.Witnessed | MemoryTags.Pinned, ulong.MaxValue))];
        InspectedOpinion[] opinions = [.. Enumerable.Range(0, CreatureInspection.MaxOpinions)
            .Select(_ => new InspectedOpinion(far, new Opinion(Opinion.Min, Opinion.Max, Opinion.Min, Opinion.Max)))];
        GoalScore[] goals = [.. Enumerable.Range(0, CreatureInspection.MaxGoals).Select(_ => new GoalScore(ushort.MaxValue, int.MaxValue))];
        var extreme = new Personality(-100, 100, -100, 100);
        return new CreatureInspection(
            ulong.MaxValue, new StableId(ulong.MaxValue), ushort.MaxValue, int.MinValue, AiLodTier.Abstract, new Vector3(-1e9f, 1e9f, float.MaxValue),
            Emote.Grief, ushort.MaxValue, far, 1000, extreme, extreme, new EmotionState(100, 100, 100, 100, 100), [100, 100, 100, 100, 100],
            ICreatureMindStore.CoreCapacity, ICreatureMindStore.EpisodicCapacity, memories, opinions, goals, ushort.MaxValue, BehaviourTreeDefinition.MaxNodes - 1,
            new InspectedCommand(CompanionCommand.Retreat, CommandResponse.Refuse, int.MinValue, ObedienceReason.BetterTactic, ulong.MaxValue));
    }

    private static byte[] Encode(DevInspection message)
    {
        var buffer = new byte[Protocol.MaxMessageSize];
        var writer = new MessageWriter(buffer);
        new DevInspectionCodec().Write(ref writer, message);
        return writer.WrittenSpan.ToArray();
    }

    private static T Read<T>(IMessageCodec<T> codec, byte[] bytes)
        where T : struct
    {
        var reader = new MessageReader(bytes);
        Protocol.ReadHeader(ref reader);
        return codec.Read(ref reader);
    }

    private static T RoundTrip<T>(IMessageCodec<T> codec, T message)
        where T : struct
    {
        var buffer = new byte[Protocol.MaxMessageSize];
        var writer = new MessageWriter(buffer);
        codec.Write(ref writer, message);
        return Read(codec, writer.WrittenSpan.ToArray());
    }

    private sealed class Lan : IDisposable
    {
        private readonly LoopbackTransport _serverTransport;
        private readonly List<(GameClient Client, LoopbackTransport Transport)> _clients = [];

        public Lan(bool devTools)
        {
            Settings = new GameServerSettings { DevTools = devTools };
            Network = new LoopbackNetwork(new SeededRandom(0xDE7), latencySteps: 2, 0, bufferCount: 8192);
            _serverTransport = Network.CreateEndpoint(2048);
            World = new CreatureWorld(ShippedData.Current, new CreatureWorldSettings(), seed: 5);
            Server = new GameServer(World, new MeteredTransport(_serverTransport), new DevTokenAuthenticator(Secret), Log, Settings);
        }

        public LoopbackNetwork Network { get; }

        public CreatureWorld World { get; }

        public GameServer Server { get; }

        public GameServerSettings Settings { get; }

        public RecordingLog Log { get; } = new();

        public GameClient Join(ulong account)
        {
            LoopbackTransport transport = Network.CreateEndpoint(1024);
            var client = new GameClient(transport, Log, new Hello(0, DevTokenAuthenticator.Ticket(account, Secret)), Settings.Movement);
            transport.Connect(_serverTransport);
            _clients.Add((client, transport));
            return client;
        }

        public LoopbackTransport TransportOf(GameClient client) => _clients.Single(entry => entry.Client == client).Transport;

        public void Reconnect(GameClient client)
        {
            LoopbackTransport transport = TransportOf(client);
            client.Reconnect(transport);
            transport.Connect(_serverTransport);
        }

        public StableId SpawnCreature(Vector3 position)
        {
            ushort species = World.SpeciesIndex("mossback");
            Entity creature = World.SpawnCreature(species, position, Personality.Roll(World.Data.Species[species], World.Random), group: 0);
            return World.World.GetComponent<StableId>(creature);
        }

        public void Run(int steps)
        {
            for (int i = 0; i < steps; i++)
            {
                Network.Step();
                Server.Tick();
                foreach ((GameClient client, _) in _clients)
                {
                    client.Tick(MoveInput.None);
                }
            }
        }

        public void Dispose() => World.Dispose();
    }

    private sealed class RecordingLog : ILog
    {
        public List<string> Events { get; } = [];

        public bool IsEnabled(LogLevel level) => true;

        public void Write(LogLevel level, string eventName, string message) => Events.Add(eventName);
    }
}
