using System.Numerics;
using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Progression;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.Transactions;
using SumonerWorld.Core.World;
using SumonerWorld.Scenarios;
using SumonerWorld.Tests.Core.Bonding;

namespace SumonerWorld.Tests.Core.Progression;

public sealed class PlayerProgressTests : IDisposable
{
    private const ulong Alice = 1001;
    private static readonly Vector3 Start = new(100, 0, 100);

    private readonly CreatureWorld _world = new(ShippedData.Current, new CreatureWorldSettings(), seed: 21);
    private readonly SimulationScheduler _scheduler;
    private readonly Entity _alice;

    public PlayerProgressTests()
    {
        _scheduler = new SimulationScheduler(_world.Clock, _world.Events);
        _world.CreateSystems().RegisterAll(_scheduler);
        _alice = _world.SpawnPlayerAvatar(Alice, Start);
    }

    public void Dispose() => _world.Dispose();

    [Theory]
    [InlineData(100, 50, 600)]
    [InlineData(100, 20, 600)]
    [InlineData(100, 51, 392)]
    [InlineData(100, 70, 250)]
    [InlineData(100, 90, 100)]
    [InlineData(100, 95, 0)]
    [InlineData(100, 100, 0)]
    [InlineData(100, 105, -150)]
    [InlineData(10, 5, 600)]
    [InlineData(1, 4, -90)]
    public void The_level_difference_eases_or_hardens_a_Pact_as_the_spec_table_says(int player, int creature, int bonus)
    {
        Assert.Equal(bonus, new PactConditions(SummonerClass.Elf, player, SummonerClass.Elf, creature).LevelBonus);
    }

    [Fact]
    public void A_creature_of_another_class_always_refuses_and_remembers_it()
    {
        var kit = new BondingTestKit(new Personality(0, 100, 0, 0));
        SpeciesDefinition species = BondingTestKit.Species(threshold: -1000);
        var necromancer = new PactConditions(SummonerClass.Necromancer, 50, SummonerClass.Elf, 1);

        BondingOutcome outcome = kit.Bonding.OfferPact(
            kit.Creature, species, BondingTestKit.Alice, PactTerms.Of(0), 1000, 0, out PactDecision decision, necromancer);

        Assert.Equal(BondingOutcome.WrongClass, outcome);
        Assert.True(decision.WrongClass);
        Assert.False(decision.Accepted);
        Assert.Equal(OwnershipState.Wild, kit.Bonding.GetBond(kit.Creature).State);
        Assert.Equal(1, kit.CountMemories("PactRefused", BondingTestKit.Alice, 0));
    }

    [Fact]
    public void A_much_stronger_summoner_wins_a_creature_that_would_refuse_an_equal()
    {
        var kit = new BondingTestKit(new Personality(0, 0, 0, 0));
        SpeciesDefinition species = BondingTestKit.Species(threshold: 500);

        PactDecision equal = kit.Evaluator.Evaluate(kit.Creature, species, BondingTestKit.Alice, PactTerms.Of(0), 0, 0, new PactConditions(SummonerClass.Elf, 4, SummonerClass.Elf, 4));
        PactDecision stronger = kit.Evaluator.Evaluate(kit.Creature, species, BondingTestKit.Alice, PactTerms.Of(0), 0, 0, new PactConditions(SummonerClass.Elf, 10, SummonerClass.Elf, 4));

        Assert.False(equal.Accepted);
        Assert.True(stronger.Accepted);
        Assert.Equal(600, stronger.LevelGap);
    }

    [Fact]
    public void An_offer_in_play_uses_the_players_class_and_level_and_a_sealed_Pact_gives_experience()
    {
        Entity creature = _world.SpawnCreature(_world.SpeciesIndex("bogfox"), Start + new Vector3(3, 0, 0), new Personality(0, 0, 0, 0), group: 4);
        _world.World.GetComponent<PlayerProgress>(_alice) = PlayerProgress.New with { Rank = (int)Rank.C, Level = 30, Xp = _world.Growth.XpFor(30) };

        IntentResult result = Offer(creature, sequence: 1);

        Assert.Equal(BondingOutcome.Accepted, (BondingOutcome)result.Detail);
        Assert.Equal(_world.Growth.XpFor(30) + _world.Growth.Definition.PlayerPactXp, _world.World.GetComponent<PlayerProgress>(_alice).Xp);
    }

    [Fact]
    public void A_summoner_of_another_class_is_refused_in_play_and_the_creature_turns_away()
    {
        Entity creature = _world.SpawnCreature(_world.SpeciesIndex("bogfox"), Start + new Vector3(3, 0, 0), new Personality(0, 100, 0, 0), group: 4);
        _world.World.GetComponent<PlayerProgress>(_alice) = PlayerProgress.New with { Class = (int)SummonerClass.Dwarf, Level = 10 };

        IntentResult result = Offer(creature, sequence: 1);

        Assert.Equal(BondingOutcome.WrongClass, (BondingOutcome)result.Detail);
        Assert.Equal(0, _world.World.GetComponent<PactAnswer>(creature).Accepted);
    }

    [Fact]
    public void Defeating_or_sparing_a_creature_gives_the_player_experience_up_to_the_rank_cap()
    {
        Entity wild = _world.SpawnCreature(_world.SpeciesIndex("bogfox"), Start + new Vector3(30, 0, 0), new Personality(0, 0, 0, 0), group: 4);
        int health = _world.World.GetComponent<Combatant>(_alice).MaxHealth;

        _world.Events.Publish(new CombatantDefeated(wild, 0, Alice, 0));
        _world.Events.Publish(new CreatureSpared(wild, Alice));
        _scheduler.RunTick();

        PlayerProgress progress = _world.World.GetComponent<PlayerProgress>(_alice);
        Assert.Equal(_world.Growth.Definition.PlayerDefeatXp + _world.Growth.Definition.PlayerSpareXp, progress.Xp);
        Assert.Equal(2, progress.Level);
        Assert.True(_world.World.GetComponent<Combatant>(_alice).MaxHealth > health, "a level up raises health");

        ref PlayerProgress p = ref _world.World.GetComponent<PlayerProgress>(_alice);
        PlayerGrowth.Earn(_world.Growth, ref p, ref _world.World.GetComponent<Combatant>(_alice), _world.Data.Combat.Player, 1_000_000);
        Assert.Equal(GrowthRules.Cap(Rank.F), _world.World.GetComponent<PlayerProgress>(_alice).Level);
    }

    private IntentResult Offer(Entity creature, uint sequence)
    {
        var transaction = new OfferPactTransaction(_world, new CooldownTracker(), _alice, _world.World.GetComponent<StableId>(creature), PactTerms.Of(0));
        return new TransactionProcessor(new IntentDeduplicator()).Execute(new IntentId(1, sequence), ref transaction, _world.Clock.Current.Index);
    }
}
