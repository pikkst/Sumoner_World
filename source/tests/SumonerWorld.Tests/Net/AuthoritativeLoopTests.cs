using System.Numerics;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Movement;
using SumonerWorld.Core.Randomness;
using SumonerWorld.Core.Transactions;
using SumonerWorld.Core.World;
using SumonerWorld.Net.Client;
using SumonerWorld.Net.Messages;
using SumonerWorld.Net.Server;
using SumonerWorld.Net.Sessions;
using SumonerWorld.Net.Transport;
using SumonerWorld.Scenarios;

namespace SumonerWorld.Tests.Net;

public class AuthoritativeLoopTests
{
    private const string Secret = "loop-secret";

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    public void Two_clients_predict_their_own_movement_and_end_where_the_server_has_them(int lossPercent)
    {
        using var lan = new Lan(lossPercent);
        GameClient alice = lan.Join(1001);
        GameClient bob = lan.Join(1002);
        lan.Run(30, _ => MoveInput.None);

        lan.Run(150, client => client == alice
            ? new MoveInput(1000, (short)(lan.Tick % 60 < 30 ? 300 : -300))
            : new MoveInput(-700, 700));
        lan.Run(30, _ => MoveInput.None);

        foreach ((GameClient client, ulong account) in new[] { (alice, 1001UL), (bob, 1002UL) })
        {
            Vector3 server = lan.ServerPosition(account);
            Assert.Equal(server, client.Prediction.Position);
            Assert.True(Vector3.Distance(server, lan.Settings.SpawnPoint) > 20, "The avatar moved.");
            uint inFlight = client.Prediction.LastSequence - lan.Server.LastAppliedInput(account);
            Assert.True(inFlight <= InputBatch.MaxInputs, $"{inFlight} inputs not applied yet (only the idle ones in flight).");
        }

        if (lossPercent == 0)
        {
            Assert.Equal(0, alice.Prediction.Corrections[(int)Correction.Snapped] + alice.Prediction.Corrections[(int)Correction.Smoothed]);
            Assert.True(alice.Prediction.Corrections[(int)Correction.None] > 100, "Every snapshot confirmed the prediction.");
        }
        else
        {
            Assert.True(lan.Network.Dropped > 100, "Loss was simulated.");
        }

        SnapshotEntity seenBob = alice.Entities[(SnapshotEntityKind.Player, 1002)];
        Assert.True(Vector3.Distance(seenBob.Position, lan.ServerPosition(1002)) < 0.01f, "Alice sees Bob where the server has him.");
    }

    [Fact]
    public void Memory_grove_interactions_travel_as_intents_and_the_third_hit_kills()
    {
        using var lan = new Lan(0);
        StableId creature = lan.SpawnCreature(lan.Settings.SpawnPoint + new Vector3(2.5f, 0, 0));
        Assert.True(lan.World.TryFindCreature(creature, out Entity held));
        lan.World.Snare(held, 0); // keeps it in reach: a hit creature would run away
        GameClient alice = lan.Join(1001);
        lan.Run(10, _ => MoveInput.None);

        uint feed = alice.SendIntent(IntentKind.Feed, creature.Value, 0);
        uint scare = alice.SendIntent(IntentKind.Scare, 0, 0);
        uint[] hits = new uint[3];
        for (int i = 0; i < hits.Length; i++)
        {
            hits[i] = alice.SendIntent(IntentKind.Hit, creature.Value, 0);
            lan.Run((int)HitTransaction.CooldownTicks + 1, _ => MoveInput.None);
        }

        Assert.True(alice.IntentResults[feed].IsApplied);
        Assert.Equal(IntentResult.Applied(0), alice.IntentResults[scare]);
        Assert.Equal([0, 0, 1], hits.Select(hit => (int)alice.IntentResults[hit].Detail));
        Assert.False(lan.World.TryFindCreature(creature, out _));
    }

    [Fact]
    public void An_intent_over_the_network_is_applied_once_and_resending_it_returns_the_first_result()
    {
        using var lan = new Lan(0);
        StableId creature = lan.SpawnCreature(lan.Settings.SpawnPoint + new Vector3(3, 0, 0));
        GameClient alice = lan.Join(1001);
        lan.Run(10, _ => MoveInput.None);

        uint offer = alice.SendIntent(IntentKind.OfferPact, creature.Value, 0b1);
        uint unknown = alice.SendIntent(IntentKind.OfferPact, 999_999, 0b1);
        lan.Run(10, _ => MoveInput.None);
        IntentResult first = alice.IntentResults[offer];
        alice.Resend(new IntentRequest(offer, IntentKind.OfferPact, creature.Value, 0b1));
        lan.Run(10, _ => MoveInput.None);

        Assert.True(first.IsApplied);
        Assert.Equal(first, alice.IntentResults[offer]);
        Assert.Equal(IntentRejectReason.InvalidTarget, alice.IntentResults[unknown].Reason);
        Assert.Equal(1, lan.Server.Transactions.Applied);
        Assert.Equal(1, lan.Server.Transactions.Duplicates);
    }

    [Theory]
    [InlineData(2, 0.17f, 0.23f)]
    [InlineData(20, 0.01f, 0.4f)]
    public void Another_player_moves_smoothly_on_screen(int lossPercent, float minStep, float maxStep)
    {
        using var lan = new Lan(lossPercent);
        GameClient alice = lan.Join(1001);
        GameClient bob = lan.Join(1002);
        lan.Run(30, _ => MoveInput.None);

        var shown = new List<float>();
        for (int i = 0; i < 240; i++)
        {
            lan.Run(1, client => client == bob ? new MoveInput(1000, 0) : MoveInput.None);
            if (i >= 30 && alice.Remote.TrySample(SnapshotEntityKind.Player, 1002, out RemoteState state))
            {
                shown.Add(state.Position.X);
            }
        }

        // 0.2 m per tick. At the supported 2 % loss every frame's step stays within 15 % of that (1 cm quantization and the server's
        // input catch-up move the true position unevenly too). At 20 % loss steps vary more, but the player never stops or goes back.
        Assert.Equal(210, shown.Count);
        float[] steps = [.. shown.Zip(shown.Skip(1), (a, b) => b - a)];
        Assert.InRange(steps.Min(), minStep, 0.2f);
        Assert.InRange(steps.Max(), 0.2f, maxStep);
    }

    [Fact]
    public void On_terrain_prediction_ends_exactly_where_the_server_has_the_player_and_on_the_ground()
    {
        Heightfield ground = Heightfield.Generate(9, new HeightfieldSettings { Samples = 513 });
        using var lan = new Lan(20, settings: new GameServerSettings { Movement = new MovementSettings { Ground = ground } });
        GameClient alice = lan.Join(1001);
        lan.Run(30, _ => MoveInput.None);

        lan.Run(300, _ => new MoveInput(800, (short)(lan.Tick % 90 < 45 ? 600 : -600)));
        lan.Run(30, _ => MoveInput.None);

        Vector3 server = lan.ServerPosition(1001);
        Assert.Equal(server, alice.Prediction.Position);
        Assert.Equal(ground.HeightAt(server.X, server.Z), server.Y);
        Assert.True(Vector3.Distance(server, new Vector3(256, 0, 256) with { Y = server.Y }) > 30, "the player moved");
    }

    [Fact]
    public void With_a_journal_results_wait_until_durable_and_repeats_are_not_recorded_again()
    {
        var journal = new HeldJournal();
        using var lan = new Lan(0, journal, new GameServerSettings { LastSessionId = 41 });
        StableId creature = lan.SpawnCreature(lan.Settings.SpawnPoint + new Vector3(3, 0, 0));
        GameClient alice = lan.Join(1001);
        lan.Run(10, _ => MoveInput.None);

        uint offer = alice.SendIntent(IntentKind.OfferPact, creature.Value, 0b1);
        lan.Run(10, _ => MoveInput.None);
        Assert.False(alice.IntentResults.ContainsKey(offer), "No result before the journal made it durable.");
        JournalEntry entry = Assert.Single(journal.Entries);
        Assert.Equal(new IntentId(42, offer), entry.Id);
        Assert.True(entry.IsNew);
        Assert.Equal(creature, Assert.Single(entry.Changed).Id);

        journal.ReleaseAll();
        lan.Run(5, _ => MoveInput.None);
        Assert.True(alice.IntentResults[offer].IsApplied);

        alice.Resend(new IntentRequest(offer, IntentKind.OfferPact, creature.Value, 0b1));
        lan.Run(10, _ => MoveInput.None);
        Assert.False(journal.Entries[1].IsNew);
        Assert.Empty(journal.Entries[1].Changed);
        Assert.Equal(1, lan.Server.PendingAcks);
    }

    [Fact]
    public void Snapshots_carry_everything_in_the_interest_radius_in_several_parts_and_nothing_beyond()
    {
        using var lan = new Lan(0);
        var near = new List<StableId>();
        for (int i = 0; i < 150; i++)
        {
            near.Add(lan.SpawnCreature(lan.Settings.SpawnPoint + new Vector3(i % 15, 0, i / 15)));
        }

        StableId far = lan.SpawnCreature(new Vector3(10, 0, 10));
        GameClient alice = lan.Join(1001);
        lan.Run(10, _ => MoveInput.None);
        int partsBefore = lan.Server.SnapshotPartsSent;
        lan.Run(3, _ => MoveInput.None);

        Assert.True(lan.Server.SnapshotPartsSent - partsBefore >= 4, "150 creatures need several parts per snapshot.");
        Assert.All(near, id => Assert.True(alice.Entities.ContainsKey((SnapshotEntityKind.Creature, id.Value))));
        Assert.False(alice.Entities.ContainsKey((SnapshotEntityKind.Creature, far.Value)));
    }

    [Fact]
    public void A_disconnected_avatar_lingers_30_s_and_comes_back_where_it_was_on_reconnect()
    {
        using var lan = new Lan(0);
        GameClient alice = lan.Join(1001);
        lan.Run(10, _ => MoveInput.None);
        lan.Run(60, _ => new MoveInput(1000, 0));
        lan.Run(10, _ => MoveInput.None);
        Vector3 before = lan.ServerPosition(1001);

        lan.TransportOf(alice).Disconnect(alice.Session.Connection);
        lan.Run((int)GameServer.LingerTicks - 10, _ => MoveInput.None);
        Assert.True(lan.Server.TryGetAvatar(1001, out _), "Still in the world while lingering.");
        lan.Run(20, _ => MoveInput.None);
        Assert.False(lan.Server.TryGetAvatar(1001, out _));
        Assert.Equal(1, lan.Server.PlayerCount);

        lan.Reconnect(alice);
        lan.Run(10, _ => MoveInput.None);
        lan.Run(30, _ => new MoveInput(0, 1000));
        lan.Run(10, _ => MoveInput.None);

        Assert.True(alice.Session.Session!.Value.Resumed);
        Assert.Equal(before.X, lan.ServerPosition(1001).X);
        Assert.True(lan.ServerPosition(1001).Z > before.Z + 5, "Moving again after the resume.");
        Assert.Equal(lan.ServerPosition(1001), alice.Prediction.Position);
    }

    [Fact]
    public void An_intent_whose_answer_was_lost_is_resent_on_resume_and_answered_from_the_dedupe_window()
    {
        using var lan = new Lan(0);
        StableId creature = lan.SpawnCreature(lan.Settings.SpawnPoint + new Vector3(3, 0, 0));
        GameClient alice = lan.Join(1001);
        lan.Run(10, _ => MoveInput.None);

        uint offer = alice.SendIntent(IntentKind.OfferPact, creature.Value, 0b1);
        lan.Run(4, _ => MoveInput.None);
        lan.TransportOf(alice).Disconnect(alice.Session.Connection);
        lan.Run(5, _ => MoveInput.None);
        Assert.Equal(1, alice.UnansweredIntents);

        lan.Reconnect(alice);
        lan.Run(15, _ => MoveInput.None);

        Assert.True(alice.IntentResults[offer].IsApplied);
        Assert.Equal(0, alice.UnansweredIntents);
        Assert.Equal((1, 1), (lan.Server.Transactions.Applied, lan.Server.Transactions.Duplicates));
    }

    [Fact]
    public void An_expired_session_removes_the_player_and_a_client_sending_server_messages_is_dropped()
    {
        using var lan = new Lan(0);
        GameClient alice = lan.Join(1001);
        GameClient bob = lan.Join(1002);
        lan.Run(10, _ => MoveInput.None);
        Assert.Equal(2, lan.Server.PlayerCount);

        lan.TransportOf(alice).Disconnect(alice.Session.Connection);
        var writer = new MessageWriter(new byte[Protocol.MaxMessageSize]);
        new IntentAckCodec().Write(ref writer, new IntentAck(1, IntentResult.Applied()));
        lan.TransportOf(bob).Send(bob.Session.Connection, Channel.ReliableOrdered, writer.WrittenSpan);
        lan.Run(10, _ => MoveInput.None);
        Assert.Equal(ClientSessionState.Disconnected, bob.Session.State);
        Assert.Equal(1, lan.Server.PlayerCount);

        lan.Run((int)ServerEndpoint.ReconnectWindowTicks, _ => MoveInput.None);

        Assert.Equal(0, lan.Server.PlayerCount);
        Assert.Equal(0, lan.Server.World.World.Store<PlayerAvatar>().Count);
    }

    [Fact]
    public void Saved_players_reappear_where_they_were_and_are_captured_with_the_players_in_the_world()
    {
        using var lan = new Lan(0);
        lan.Server.RestorePlayers([new PlayerSave(1001, new Vector3(100, 0, 120)), new PlayerSave(3003, new Vector3(5, 0, 5))]);
        lan.Join(1001);
        lan.Join(1002);
        lan.Run(10, _ => MoveInput.None);

        Assert.Equal(new Vector3(100, 0, 120), lan.ServerPosition(1001));
        Assert.Equal(lan.Settings.SpawnPoint, lan.ServerPosition(1002));
        Assert.Equal(
            [new PlayerSave(1001, new Vector3(100, 0, 120)), new PlayerSave(1002, lan.Settings.SpawnPoint), new PlayerSave(3003, new Vector3(5, 0, 5))],
            lan.Server.CapturePlayers());
    }

    [Fact]
    public void Inputs_far_ahead_after_an_outage_skip_forward_instead_of_stalling()
    {
        using var lan = new Lan(0);
        GameClient alice = lan.Join(1001);
        lan.Run(10, _ => MoveInput.None);

        var writer = new MessageWriter(new byte[Protocol.MaxMessageSize]);
        InputBatch.Write(ref writer, [new PlayerInput(500, 1000, 0, 0), new PlayerInput(501, 1000, 0, 0)]);
        lan.TransportOf(alice).Send(alice.Session.Connection, Channel.Unreliable, writer.WrittenSpan);
        lan.Run(6, _ => MoveInput.None);

        Assert.Equal(501U, lan.Server.LastAppliedInput(1001));
    }

    private sealed class Lan : IDisposable
    {
        private readonly LoopbackTransport _serverTransport;
        private readonly List<(GameClient Client, LoopbackTransport Transport)> _clients = [];

        public Lan(int lossPercent, ITransactionJournal? journal = null, GameServerSettings? settings = null)
        {
            Settings = settings ?? new GameServerSettings();
            Network = new LoopbackNetwork(new SeededRandom(0x10AD), latencySteps: 3, lossPercent, bufferCount: 8192);
            _serverTransport = Network.CreateEndpoint(2048);
            World = new CreatureWorld(ShippedData.Current, new CreatureWorldSettings(), seed: 11);
            Server = new GameServer(World, _serverTransport, new DevTokenAuthenticator(Secret), NullLog.Instance, Settings, journal: journal);
        }

        public LoopbackNetwork Network { get; }

        public CreatureWorld World { get; }

        public GameServer Server { get; }

        public GameServerSettings Settings { get; }

        public int Tick { get; private set; }

        public GameClient Join(ulong account)
        {
            LoopbackTransport transport = Network.CreateEndpoint(1024);
            var client = new GameClient(transport, NullLog.Instance, new Hello(0, DevTokenAuthenticator.Ticket(account, Secret)), Settings.Movement);
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

        public Vector3 ServerPosition(ulong account)
        {
            Assert.True(Server.TryGetAvatar(account, out Entity avatar));
            return World.World.GetComponent<Position>(avatar).Value;
        }

        public void Run(int steps, Func<GameClient, MoveInput> input)
        {
            for (int i = 0; i < steps; i++)
            {
                Network.Step();
                Server.Tick();
                foreach ((GameClient client, _) in _clients)
                {
                    client.Tick(input(client));
                    client.Remote.Advance(1.0 / 30);
                }

                Tick++;
            }
        }

        public void Dispose() => World.Dispose();
    }

    private sealed class HeldJournal : ITransactionJournal
    {
        private readonly Queue<IntentId> _durable = new();
        private int _released;

        public List<JournalEntry> Entries { get; } = [];

        public void Append(JournalEntry entry) => Entries.Add(entry);

        public bool TryTakeDurable(out IntentId id) => _durable.TryDequeue(out id);

        public void ReleaseAll()
        {
            for (; _released < Entries.Count; _released++)
            {
                _durable.Enqueue(Entries[_released].Id);
            }
        }
    }

    private sealed class NullLog : ILog
    {
        public static readonly NullLog Instance = new();

        public bool IsEnabled(LogLevel level) => false;

        public void Write(LogLevel level, string eventName, string message)
        {
        }
    }
}
