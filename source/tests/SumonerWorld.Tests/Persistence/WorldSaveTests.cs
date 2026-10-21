using System.Numerics;
using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Core.Ecosystem;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.Transactions;
using SumonerWorld.Core.World;
using SumonerWorld.Persistence;
using SumonerWorld.Persistence.Backups;
using SumonerWorld.Persistence.Schema;
using SumonerWorld.Scenarios;

namespace SumonerWorld.Tests.Persistence;

public sealed class WorldSaveTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sw-save-" + Guid.NewGuid().ToString("N"));

    public WorldSaveTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void A_lived_in_world_survives_capture_save_load_and_restore_unchanged()
    {
        using CreatureWorld original = LivedInWorld();
        WorldSnapshot captured = WorldSnapshots.Capture(original);
        Assert.Contains(captured.Creatures, creature => creature.Mind.EpisodicRecords.Count > 0);
        Assert.Contains(captured.Creatures, creature => creature.Bond.State != OwnershipState.Wild);
        Assert.NotEmpty(captured.Reputation);

        PlayerSave[] players =
        [
            new PlayerSave(1001, new Vector3(250, 0, 260), new SumonerWorld.Core.Progression.PlayerProgress(0, 2, 17, 2_900)),
            new PlayerSave(1002, new Vector3(10, 0, 20)),
        ];
        using (WorldStore store = Open())
        {
            Assert.Null(store.LoadWorld());
            using IWorldTransaction transaction = store.Begin();
            transaction.SaveWorld(captured, players);
            transaction.Commit();
        }

        SavedWorld loaded;
        using (WorldStore store = Open())
        {
            loaded = store.LoadWorld()!;
        }

        AssertSame(captured, loaded.World);
        Assert.Equal(players, loaded.Players);
        using CreatureWorld restored = WorldSnapshots.Restore(ShippedData.Current, new CreatureWorldSettings(), loaded.World);
        AssertSame(captured, WorldSnapshots.Capture(restored));
        Assert.True(restored.TryFindCreature(captured.Creatures[0].Id, out _));
    }

    [Fact]
    public void A_restored_world_runs_deterministically_and_keeps_issuing_new_ids()
    {
        using CreatureWorld original = LivedInWorld();
        WorldSnapshot snapshot = WorldSnapshots.Capture(original);

        ulong RunRestored()
        {
            using CreatureWorld world = WorldSnapshots.Restore(ShippedData.Current, new CreatureWorldSettings(), snapshot);
            world.SpawnPlayerAvatar(1001, new Vector3(256, 0, 256));
            var scheduler = new SimulationScheduler(world.Clock, world.Events);
            world.CreateSystems().RegisterAll(scheduler);
            for (int i = 0; i < 300; i++)
            {
                scheduler.RunTick();
            }

            Entity spawned = world.SpawnCreature(0, new Vector3(10, 0, 10), new Personality(0, 0, 0, 0), 0);
            Assert.True(world.World.GetComponent<StableId>(spawned).Value > snapshot.LastStableId);
            var hash = default(StateHash);
            world.AddTo(ref hash, world.Services.Bonding);
            return hash.Value;
        }

        Assert.Equal(RunRestored(), RunRestored());
    }

    [Fact]
    public void A_v1_world_is_backed_up_and_migrated_and_the_v2_fixture_loads_its_creature()
    {
        string v1 = CopyFixture("world-v1.db");
        var backups = new WorldBackups(Path.Combine(_directory, "backups"));
        using (WorldStore migrated = WorldStore.Open(v1, backups))
        {
            Assert.Equal(Migrations.Latest, migrated.SchemaVersion);
            Assert.Null(migrated.LoadWorld());
        }

        Assert.Single(backups.List(), path => path.EndsWith("pre-migration-v1.db", StringComparison.Ordinal));

        using WorldStore v2 = WorldStore.Open(CopyFixture("world-v2.db"), backups);
        SavedWorld world = v2.LoadWorld()!;
        CreatureSave creature = Assert.Single(world.World.Creatures);
        Assert.Equal((17UL, "mossback", 3), (creature.Id.Value, creature.Species, creature.Group));
        Assert.Equal(new Personality(12, -20, 30, -38), creature.Mind.Current);
        Assert.Single(creature.Mind.EpisodicRecords);
        Assert.Equal(0xDEADBEEFUL, world.World.RandomState);
        Assert.Equal(new PlayerSave(1001, new Vector3(250, 0, 260)), Assert.Single(world.Players));
    }

    [Fact]
    public void The_checkpoint_writer_saves_the_newest_snapshot_in_the_background()
    {
        using CreatureWorld world = LivedInWorld();
        using WorldStore store = Open();
        using (var writer = new PersistenceWriter(store, new SilentLog()))
        {
            for (int i = 0; i < 5; i++)
            {
                world.Clock.Advance();
                writer.Submit(WorldSnapshots.Capture(world), []);
            }

            Assert.True(writer.Flush(TimeSpan.FromSeconds(10)));
            Assert.Equal((long)world.Clock.Current.Index, writer.LastSavedTick);
            Assert.Null(writer.LastError);
        }

        Assert.Equal(world.Clock.Current.Index, store.LoadWorld()!.World.Tick);
    }

    [Fact]
    public void Journal_entries_are_durable_in_order_with_their_dedupe_records_and_a_newer_checkpoint_replaces_a_waiting_one()
    {
        using CreatureWorld world = LivedInWorld();
        CreatureSave changed = WorldSnapshots.Capture(world).Creatures[0];
        using WorldStore store = Open();
        var durable = new List<IntentId>();
        using (var writer = new PersistenceWriter(store, new SilentLog()))
        {
            writer.Append(new JournalEntry(new IntentId(5, 1), IntentResult.Applied(1), 10, true, [changed]));
            writer.Submit(WorldSnapshots.Capture(world), []);
            world.Clock.Advance();
            writer.Submit(WorldSnapshots.Capture(world), []);
            writer.Append(new JournalEntry(new IntentId(5, 2), IntentResult.Reject(IntentRejectReason.OutOfRange), 11, true, []));
            writer.Append(new JournalEntry(new IntentId(5, 2), IntentResult.Reject(IntentRejectReason.OutOfRange), 12, false, []));
            Assert.True(writer.Flush(TimeSpan.FromSeconds(10)));
            while (writer.TryTakeDurable(out IntentId id))
            {
                durable.Add(id);
            }

            Assert.Null(writer.LastError);
            Assert.Equal((long)world.Clock.Current.Index, writer.LastSavedTick);
            Assert.Equal(3, writer.Journaled);
        }

        Assert.Equal([new IntentId(5, 1), new IntentId(5, 2), new IntentId(5, 2)], durable);
        Assert.True(store.TryGetIntent(new IntentId(5, 2), out IntentResult rejected));
        Assert.Equal(IntentRejectReason.OutOfRange, rejected.Reason);
        Assert.Equal(5UL, store.MaxIntentSession());
        Assert.Contains(store.LoadWorld()!.World.Creatures, creature => creature.Id == changed.Id);
    }

    private static CreatureWorld LivedInWorld()
    {
        var world = new CreatureWorld(ShippedData.Current, new CreatureWorldSettings(), seed: 21);
        ushort species = world.SpeciesIndex("mossback");
        for (int i = 0; i < 40; i++)
        {
            world.SpawnCreature(species, new Vector3(236 + (i % 8 * 5), 0, 236 + (i / 8 * 5)), Personality.Roll(world.Data.Species[species], world.Random), i % 4);
        }

        Entity player = world.SpawnPlayerAvatar(1001, new Vector3(256, 0, 256));
        var scheduler = new SimulationScheduler(world.Clock, world.Events);
        world.CreateSystems().RegisterAll(scheduler);
        for (int i = 0; i < 600; i++)
        {
            scheduler.RunTick();
        }

        world.Reputation.Record(world.Rules.FindByKey("KilledKin")!.Value, new ReputationKey(species, 0, 1001));
        world.Reputation.Flush();
        world.Jobs.ApplyCompleted();
        ushort fedBy = world.Rules.FindByKey("FedBy")!.Value;
        ReadOnlySpan<MindRef> minds = world.World.Store<MindRef>().Components;
        for (int i = 0; i < minds.Length; i += 3)
        {
            world.Minds.Remember(minds[i].Handle, fedBy, new MemorySubject(SubjectKind.Player, 1001), world.Clock.Current.Index);
        }

        Entity first = world.World.Store<MindRef>().Entities[0];
        world.Services.Bonding.Restore(
            world.World.GetComponent<MindRef>(first).Handle,
            new BondState(OwnershipState.Pacted, 1001, PactTerms.Of(0), 0, 1, false, 500, 0, false));
        world.World.GetComponent<SumonerWorld.Core.Progression.CreatureGrowth>(first) = new(1, 7, 2, 300, 15, 40, 2_500);
        world.World.GetComponent<SumonerWorld.Core.Progression.CreatureSkills>(first) = new(0b101, 9_000, 0, 2, 1);
        world.DespawnPlayerAvatar(player);
        return world;
    }

    private static void AssertSame(WorldSnapshot expected, WorldSnapshot actual)
    {
        Assert.Equal((expected.Tick, expected.RandomState, expected.LastStableId), (actual.Tick, actual.RandomState, actual.LastStableId));
        Assert.Equal(expected.Reputation, actual.Reputation);
        Assert.Equal(expected.Creatures.Count, actual.Creatures.Count);
        for (int i = 0; i < expected.Creatures.Count; i++)
        {
            CreatureSave a = expected.Creatures[i];
            CreatureSave b = actual.Creatures[i];
            Assert.Equal((a.Id, a.Species, a.Position, a.Facing, a.Group, a.Bond, a.Growth, a.Skills), (b.Id, b.Species, b.Position, b.Facing, b.Group, b.Bond, b.Growth, b.Skills));
            Assert.Equal((a.Mind.Birth, a.Mind.Current), (b.Mind.Birth, b.Mind.Current));
            Assert.Equal(a.Mind.CoreRecords, b.Mind.CoreRecords);
            Assert.Equal(a.Mind.EpisodicRecords, b.Mind.EpisodicRecords);
            Assert.Equal(a.Mind.Baselines, b.Mind.Baselines);
            Assert.Equal(a.Mind.Needs, b.Mind.Needs);
        }
    }

    private string CopyFixture(string name)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string fixture = Path.Combine(directory.FullName, "tests", "fixtures", "saves", name);
            if (File.Exists(fixture))
            {
                string copy = Path.Combine(_directory, name);
                File.Copy(fixture, copy);
                return copy;
            }
        }

        throw new FileNotFoundException($"Fixture {name} not found.");
    }

    private WorldStore Open() => WorldStore.Open(Path.Combine(_directory, "world.db"), new WorldBackups(Path.Combine(_directory, "backups")));

    private sealed class SilentLog : ILog
    {
        public bool IsEnabled(LogLevel level) => false;

        public void Write(LogLevel level, string eventName, string message)
        {
        }
    }
}
