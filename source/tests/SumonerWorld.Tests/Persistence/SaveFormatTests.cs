using System.Numerics;
using System.Reflection;
using MessagePack;
using SumonerWorld.Core.Academy;
using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Items;
using SumonerWorld.Core.Progression;
using SumonerWorld.Core.Quests;
using SumonerWorld.Core.World;
using SumonerWorld.Persistence;
using SumonerWorld.Persistence.Backups;
using SumonerWorld.Persistence.Records;
using SumonerWorld.Persistence.Schema;
using SumonerWorld.Scenarios;

namespace SumonerWorld.Tests.Persistence;

// Versioned saves (M2-15, R26): saved records only ever gain keys; a save written by every milestone keeps loading.
public sealed class SaveFormatTests : IDisposable
{
    private const string GoldenFixture = "world-m2.db";
    private const string WriteFixturesVariable = "SW_WRITE_SAVE_FIXTURES";

    // Every key every shipped record uses, by name. A key may be added here; one may never change or be removed (old saves
    // would then read the wrong field).
    private static readonly Dictionary<Type, string[]> FrozenKeys = new()
    {
        [typeof(PlayerRecord)] =
        [
            "X", "Y", "Z", "Class", "Rank", "Level", "Xp", "Gold", "Items", "Equipped", "Quests", "AcademyAnswers", "AcademyBonus", "AcademyRanked",
            "AcademyMoments", "AcademyFirstCreature",
        ],
        [typeof(QuestRecord)] = ["Quest", "Step", "Counts", "Done"],
        [typeof(SlotRecord)] = ["Item", "Count"],
        [typeof(CreatureRecord)] = ["Species", "Position", "Facing", "Group", "Mind", "Bond", "Growth", "Skills", "Combos", "Equipped", "Bag"],
        [typeof(CombosRecord)] = ["Learned", "Landed", "SelfStarted"],
        [typeof(SkillsRecord)] = ["Learned", "Spent", "UnspentSince", "PickedItself"],
        [typeof(GrowthRecord)] = ["Tier", "Level", "Stage", "CombatXp", "WorkXp", "TravelXp", "TravelCm"],
        [typeof(MindRecord)] = ["Birth", "Current", "Core", "Episodic", "Baselines", "Needs"],
        [typeof(BondRecord)] = ["State", "Owner", "Terms", "BrokenTerms", "BondLevel", "Summoned", "PactTick", "DistrustSinceTick", "Distrustful"],
        [typeof(ItemRecord)] = ["Kind", "Quantity"],
    };

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sw-format-" + Guid.NewGuid().ToString("N"));

    public SaveFormatTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Saved_record_keys_are_never_changed_or_reused()
    {
        foreach ((Type type, string[] names) in FrozenKeys)
        {
            Dictionary<int, string> keys = type.GetProperties()
                .Where(property => property.GetCustomAttribute<KeyAttribute>() is not null)
                .ToDictionary(property => property.GetCustomAttribute<KeyAttribute>()!.IntKey!.Value, property => property.Name);
            for (int key = 0; key < names.Length; key++)
            {
                Assert.True(keys.TryGetValue(key, out string? name) && name == names[key], $"{type.Name} key {key} must stay '{names[key]}'.");
            }

            Assert.True(keys.Count == names.Length, $"{type.Name} has a key not listed here: add it to {nameof(FrozenKeys)} (only ever at the end).");
        }
    }

    [Fact]
    public void An_early_m2_save_restores_into_today_s_world_with_defaults_for_everything_newer()
    {
        using WorldStore store = WorldStore.Open(CopyFixture("world-v2.db"), new WorldBackups(Path.Combine(_directory, "backups")));
        SavedWorld saved = store.LoadWorld()!;

        using CreatureWorld world = WorldSnapshots.Restore(ShippedData.Current, new CreatureWorldSettings(), saved.World);
        Assert.True(world.TryFindCreature(new StableId(17), out Entity creature));
        Assert.Equal(default(Equipment), world.World.GetComponent<Equipment>(creature));
        Assert.True(world.World.GetComponent<CreatureGrowth>(creature).Level >= 1);
        Assert.Null(world.Economy.CreatureBags.Find(17));

        PlayerSave player = Assert.Single(saved.Players);
        Assert.Equal((null, null, null), (player.Progress, player.Inventory, player.Academy));
        Assert.Empty(player.Quests ?? []);
        world.Economy.Quests.Import(player.Account, player.Quests, world.Data);
        world.Economy.Academy.Import(player.Account, player.Academy);
        Assert.Empty(world.Economy.Quests.Export(player.Account, world.Data));
        Assert.Null(world.Economy.Academy.Export(player.Account));
    }

    [Fact]
    public void A_save_written_in_m2_loads_every_kind_of_state()
    {
        string path = Environment.GetEnvironmentVariable(WriteFixturesVariable) == "1" ? WriteGolden() : CopyFixture(GoldenFixture);
        using WorldStore store = WorldStore.Open(path, new WorldBackups(Path.Combine(_directory, "backups")));
        Assert.Equal(Migrations.Latest, store.SchemaVersion);
        SavedWorld saved = store.LoadWorld()!;

        AssertGoldenPlayer(Assert.Single(saved.Players));
        AssertGoldenCreature(saved);
    }

    private static void AssertGoldenPlayer(PlayerSave player)
    {
        Assert.Equal(new PlayerProgress((int)SummonerClass.Elf, (int)Rank.D, 7, 900), player.Progress);
        Assert.Equal(new InventorySave(55, [new SavedItem("fox_fur", 3), new SavedItem(string.Empty, 0), new SavedItem("berry_meal", 2)]), player.Inventory);
        Assert.Equal(["wooden_staff", string.Empty, string.Empty, string.Empty, "mossback_charm", string.Empty], player.Equipped);
        Assert.Equal([new QuestSave("berries_for_the_road", 0, [3, 0, 0, 0], false), new QuestSave("the_ranking", 0, [], true)], player.Quests);
        Assert.Equal(new AcademySave([1, 2, 3, 4, 1, 2, 3, 4], 12, true, 7, 41), player.Academy);
    }

    private static void AssertGoldenCreature(SavedWorld saved)
    {
        CreatureSave creature = Assert.Single(saved.World.Creatures);
        Assert.Equal(("mossback", OwnershipState.Pacted, 1001UL), (creature.Species, creature.Bond.State, creature.Bond.Owner));
        Assert.Equal(new CreatureGrowth(3, 6, 1, 120, 10, 5, 2_000), creature.Growth);
        Assert.Equal(1UL, creature.Skills!.Value.Learned);
        Assert.Equal(1, creature.Combos!.Value.Learned);
        Assert.Equal([string.Empty, string.Empty, string.Empty, "leather_vest", string.Empty, string.Empty], creature.Equipped);
        Assert.Equal([new SavedItem("owl_feather", 4)], creature.Bag);

        using CreatureWorld world = WorldSnapshots.Restore(ShippedData.Current, new CreatureWorldSettings(), saved.World);
        Assert.True(world.TryFindCreature(creature.Id, out Entity restored));
        Assert.Equal(world.Data.ItemIndex("leather_vest"), world.World.GetComponent<Equipment>(restored).ItemAt(EquipPosition.Body));
        Assert.Equal(4, world.Economy.CreatureBags.Find(creature.Id.Value)!.CountOf(world.Data.ItemIndex("owl_feather")));
    }

    // Builds the M2 save from scratch and writes it as the fixture (run once with SW_WRITE_SAVE_FIXTURES=1, then commit it).
    private string WriteGolden()
    {
        using var world = new CreatureWorld(ShippedData.Current, new CreatureWorldSettings(), seed: 15);
        Entity creature = world.SpawnCreature(world.SpeciesIndex("mossback"), new Vector3(250, 0, 262), new Personality(10, 40, 20, -30), group: 4);
        world.Services.Bonding.Restore(world.World.GetComponent<MindRef>(creature).Handle, new BondState(OwnershipState.Pacted, 1001, PactTerms.Of(1), 0, 2, false, 10, 0, false));
        world.World.GetComponent<CreatureGrowth>(creature) = new CreatureGrowth(3, 6, 1, 120, 10, 5, 2_000);
        world.World.GetComponent<CreatureSkills>(creature) = world.World.GetComponent<CreatureSkills>(creature) with { Learned = 1, Spent = 1 };
        world.World.GetComponent<CreatureCombos>(creature) = world.World.GetComponent<CreatureCombos>(creature) with { Learned = 1 };
        world.World.GetComponent<Equipment>(creature) = default(Equipment).With(EquipPosition.Body, world.Data.ItemIndex("leather_vest"));
        world.Economy.CreatureBags.Of(world.World.GetComponent<StableId>(creature).Value, SumonerWorld.Core.Data.Definitions.BodyType.Quadruped)
            .Add(world.Data, [new ItemCount(world.Data.ItemIndex("owl_feather"), 4)]);

        return WriteGolden(WorldSnapshots.Capture(world));
    }

    private string WriteGolden(WorldSnapshot snapshot)
    {
        PlayerSave player = new(
            1001,
            new Vector3(256, 0, 256),
            new PlayerProgress((int)SummonerClass.Elf, (int)Rank.D, 7, 900),
            new InventorySave(55, [new SavedItem("fox_fur", 3), new SavedItem(string.Empty, 0), new SavedItem("berry_meal", 2)]),
            ["wooden_staff", string.Empty, string.Empty, string.Empty, "mossback_charm", string.Empty],
            [new QuestSave("berries_for_the_road", 0, [3, 0, 0, 0], false), new QuestSave("the_ranking", 0, [], true)],
            new AcademySave([1, 2, 3, 4, 1, 2, 3, 4], 12, true, 7, 41));
        string path = Path.Combine(FixtureDirectory(), GoldenFixture);
        File.Delete(path);
        using (WorldStore store = WorldStore.Open(path, new WorldBackups(Path.Combine(_directory, "backups"))))
        {
            using IWorldTransaction transaction = store.Begin();
            transaction.SaveWorld(snapshot, [player]);
            transaction.Commit();
        }

        foreach (string sidecar in new[] { "-wal", "-shm" })
        {
            File.Delete(path + sidecar);
        }

        return CopyFixture(GoldenFixture);
    }

    private string CopyFixture(string name)
    {
        string copy = Path.Combine(_directory, name);
        File.Copy(Path.Combine(FixtureDirectory(), name), copy);
        return copy;
    }

    private static string FixtureDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string fixtures = Path.Combine(directory.FullName, "tests", "fixtures", "saves");
            if (Directory.Exists(fixtures))
            {
                return fixtures;
            }
        }

        throw new DirectoryNotFoundException("tests/fixtures/saves not found.");
    }
}
