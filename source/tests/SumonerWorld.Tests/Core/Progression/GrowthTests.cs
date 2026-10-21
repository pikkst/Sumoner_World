using System.Numerics;
using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Progression;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.World;
using SumonerWorld.Scenarios;

namespace SumonerWorld.Tests.Core.Progression;

public sealed class GrowthTests : IDisposable
{
    private const ulong Alice = 1001;
    private static readonly Vector3 Start = new(100, 0, 100);

    private readonly CreatureWorld _world = new(ShippedData.Current, new CreatureWorldSettings(), seed: 12);
    private readonly SimulationScheduler _scheduler;
    private readonly Entity _alice;

    public GrowthTests()
    {
        _scheduler = new SimulationScheduler(_world.Clock, _world.Events);
        _world.CreateSystems().RegisterAll(_scheduler);
        _alice = _world.SpawnPlayerAvatar(Alice, Start);
    }

    public void Dispose() => _world.Dispose();

    [Fact]
    public void The_level_curve_follows_the_spec_and_the_tier_caps_it()
    {
        GrowthRules rules = _world.Growth;

        Assert.Equal((0, 40, 320, 1_080), (rules.XpFor(1), rules.XpFor(2), rules.XpFor(5), rules.XpFor(10)));
        Assert.Equal((4, 5), (rules.LevelFor(319, Rank.F), rules.LevelFor(320, Rank.F)));
        Assert.Equal(10, rules.LevelFor(1_000_000, Rank.F));
        Assert.Equal(100, rules.LevelFor(1_000_000, Rank.God));
        Assert.Equal((10, 40, 100), (GrowthRules.Cap(Rank.F), GrowthRules.Cap(Rank.C), GrowthRules.Cap(Rank.God)));
    }

    [Fact]
    public void Wild_creatures_get_their_species_tier_and_a_level_in_its_range_with_stats_to_match()
    {
        SpeciesDefinition mossback = _world.Data.Species[_world.SpeciesIndex("mossback")];
        var levels = new HashSet<int>();
        for (int i = 0; i < 30; i++)
        {
            Entity creature = Spawn("mossback", Start + new Vector3(i * 3, 0, 20));
            CreatureGrowth growth = _world.World.GetComponent<CreatureGrowth>(creature);
            levels.Add(growth.Level);
            Assert.Equal((int)mossback.Tier, growth.Tier);
            Assert.Equal((int)LifeStage.Grown, growth.Stage);
            Assert.InRange(growth.Level, mossback.Levels.Min, mossback.Levels.Max);
            int expected = mossback.Combat.Health * (100 + (6 * (growth.Level - 1))) / 100;
            Assert.Equal(expected, _world.World.GetComponent<Combatant>(creature).MaxHealth);
        }

        Assert.True(levels.Count > 1, "levels vary between creatures");
    }

    [Fact]
    public void A_companion_earns_combat_and_travel_experience_and_levels_up_keeping_its_health_share()
    {
        Entity fox = Companion("bogfox", Start + new Vector3(3, 0, 0), stage: LifeStage.Young);
        ref Combatant combatant = ref _world.World.GetComponent<Combatant>(fox);
        combatant.Health = combatant.MaxHealth / 2;
        int maxBefore = combatant.MaxHealth;
        Entity wild = Spawn("mossback", Start + new Vector3(-3, 0, 0));

        _world.Events.Publish(new CombatHit(fox, wild, 0, 60, 100));
        _world.Events.Publish(new CombatHit(wild, _alice, 0, 40, 60));
        _scheduler.RunTick();

        CreatureGrowth growth = _world.World.GetComponent<CreatureGrowth>(fox);
        Assert.Equal(30, growth.CombatXp);
        Assert.Equal(1, growth.Level);
        Assert.Equal(0, _world.World.GetComponent<CreatureGrowth>(wild).CombatXp);

        _world.Events.Publish(new CombatHit(wild, fox, 0, 20, 50));
        _scheduler.RunTick();

        Combatant after = _world.World.GetComponent<Combatant>(fox);
        Assert.Equal(2, _world.World.GetComponent<CreatureGrowth>(fox).Level);
        Assert.True(after.MaxHealth > maxBefore);
        Assert.InRange(after.Health * 100 / after.MaxHealth, 45, 55);

        for (int i = 0; i < 400; i++)
        {
            Vector3 to = _world.World.GetComponent<Position>(_alice).Value + new Vector3(0, 0, 0.2f);
            _world.World.GetComponent<Position>(_alice) = new Position(to);
            _world.Grid.Move(_alice, to);
            _scheduler.RunTick();
        }

        Assert.True(_world.World.GetComponent<CreatureGrowth>(fox).TravelXp >= 1, "80 m walked at her side is at least one point of travel");
    }

    [Fact]
    public void A_young_companion_grows_up_while_its_summoner_rests_and_remembers_it()
    {
        Entity fox = Companion("bogfox", Start + new Vector3(3, 0, 0), stage: LifeStage.Young, bondLevel: 2);
        ref CreatureGrowth growth = ref _world.World.GetComponent<CreatureGrowth>(fox);
        growth.CombatXp = _world.Growth.XpFor(4);
        growth.Level = 4;
        int healthYoung = _world.World.GetComponent<Combatant>(fox).MaxHealth;
        int memoriesBefore = _world.Minds.TotalRecords;
        int sawAffection = 0;

        for (int i = 0; i < _world.Growth.Definition.RestTicks + 5; i++)
        {
            _scheduler.RunTick();
            sawAffection += _world.World.GetComponent<CurrentEmote>(fox).Value == Emote.Affection ? 1 : 0;
        }

        Assert.Equal((int)LifeStage.Grown, _world.World.GetComponent<CreatureGrowth>(fox).Stage);
        Assert.True(_world.World.GetComponent<Combatant>(fox).MaxHealth > healthYoung, "a grown body has more health");
        Assert.True(_world.Minds.TotalRecords > memoriesBefore, "it remembers growing up");
        Assert.True(sawAffection > 0);
    }

    [Theory]
    [InlineData(3, 2)]
    [InlineData(4, 1)]
    public void It_does_not_grow_up_before_its_level_and_bond_allow(int level, int bondLevel)
    {
        Entity fox = Companion("bogfox", Start + new Vector3(3, 0, 0), stage: LifeStage.Young, bondLevel: (byte)bondLevel);
        _world.World.GetComponent<CreatureGrowth>(fox).Level = level;

        for (int i = 0; i < _world.Growth.Definition.RestTicks + 30; i++)
        {
            _scheduler.RunTick();
        }

        Assert.Equal((int)LifeStage.Young, _world.World.GetComponent<CreatureGrowth>(fox).Stage);
    }

    [Fact]
    public void A_creature_saved_before_growth_existed_gets_level_one_young_when_pacted()
    {
        SpeciesDefinition owl = _world.Data.Species[_world.SpeciesIndex("hollow_owl")];

        Assert.Equal(new CreatureGrowth((int)owl.Tier, 1, (int)LifeStage.Young, 0, 0, 0, 0), GrowthRules.Migrated(owl, 77, pacted: true));
        Assert.Equal(GrowthRules.Wild(owl, 77), GrowthRules.Migrated(owl, 77, pacted: false));
    }

    private Entity Spawn(string species, Vector3 at) => _world.SpawnCreature(_world.SpeciesIndex(species), at, new Personality(0, 0, 0, 0), group: 9);

    private Entity Companion(string species, Vector3 at, LifeStage stage, byte bondLevel = 1)
    {
        Entity creature = _world.SpawnCreature(_world.SpeciesIndex(species), at, new Personality(0, 100, 0, 0), group: 7);
        ref CreatureGrowth growth = ref _world.World.GetComponent<CreatureGrowth>(creature);
        growth = growth with { Level = 1, Stage = (int)stage };
        _world.Growth.Apply(ref _world.World.GetComponent<Combatant>(creature), _world.Data.Species[_world.SpeciesIndex(species)].Combat, growth);
        MindHandle mind = _world.World.GetComponent<MindRef>(creature).Handle;
        _world.Services.Bonding.Restore(mind, new BondState(OwnershipState.Pacted, Alice, PactTerms.Of(0), 0, bondLevel, true, 0, 0, false));
        return creature;
    }
}
