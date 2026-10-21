using System.Numerics;
using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.Transactions;
using SumonerWorld.Core.World;
using SumonerWorld.Scenarios;

namespace SumonerWorld.Tests.Core.Combat;

public sealed class ComboTests : IDisposable
{
    private const ulong Alice = 1001;
    private static readonly Vector3 Start = new(100, 0, 100);

    private readonly CreatureWorld _world = new(ShippedData.Current, new CreatureWorldSettings(), seed: 51);
    private readonly SimulationScheduler _scheduler;
    private readonly CreatureSystems _systems;
    private readonly Entity _alice;

    public ComboTests()
    {
        _scheduler = new SimulationScheduler(_world.Clock, _world.Events);
        _systems = _world.CreateSystems();
        _systems.RegisterAll(_scheduler);
        _alice = _world.SpawnPlayerAvatar(Alice, Start);
    }

    public void Dispose() => _world.Dispose();

    [Fact]
    public void A_charge_into_a_rooted_target_does_double_damage_and_knocks_it_down()
    {
        Entity bull = Companion(Start + new Vector3(0, 0, 3), summoned: false);
        Entity rooted = Target(Start + new Vector3(0, 0, 6));
        Entity free = Target(Start + new Vector3(30, 0, 6));
        Entity bull2 = Companion(Start + new Vector3(30, 0, 3), summoned: false);

        StatusRules.Apply(_world.World, _world.Data, rooted, "rooted", _world.Clock.Current.Index);
        _world.Snare(free, _world.Clock.Current.Index); // held still too, without the rooted effect
        Charge(bull, rooted);
        Charge(bull2, free);
        Run(40);

        int doubled = 1000 - Health(rooted);
        int normal = 1000 - Health(free);
        Assert.True(normal > 0, "the plain charge hit");
        Assert.Equal(normal * 2, doubled);
        Assert.Equal(1, _world.World.GetComponent<CreatureCombos>(bull).Landed0);
        Assert.Equal(0, _world.World.GetComponent<CreatureCombos>(bull2).Landed0);
    }

    [Fact]
    public void A_companion_learns_the_combo_after_landing_it_five_times_and_a_wild_creature_never_does()
    {
        Entity bull = Companion(Start + new Vector3(0, 0, 3), summoned: false);
        Entity wild = _world.SpawnCreature(_world.SpeciesIndex("mossback"), Start + new Vector3(20, 0, 3), new Personality(0, 0, 0, 0), group: 9);
        int learnAfter = _world.Data.Combos[0].LearnAfter;

        for (int i = 0; i < learnAfter; i++)
        {
            Assert.False(_world.World.GetComponent<CreatureCombos>(bull).Knows(0), $"not yet after {i}");
            _world.Events.Publish(new ComboLanded(bull, default, 0));
            _world.Events.Publish(new ComboLanded(wild, default, 0));
            _scheduler.RunTick();
        }

        Assert.True(_world.World.GetComponent<CreatureCombos>(bull).Knows(0));
        Assert.False(_world.World.GetComponent<CreatureCombos>(wild).Knows(0));
    }

    [Fact]
    public void A_companion_that_knows_the_combo_answers_its_summoners_entangle_by_itself_and_shows_it_learned_her_style()
    {
        Entity bull = Companion(Start + new Vector3(3, 0, 0), summoned: true);
        _world.World.GetComponent<CreatureCombos>(bull) = default(CreatureCombos) with { Learned = 1, Landed0 = 5 };
        Entity learner = Companion(Start + new Vector3(-3, 0, 0), summoned: true);
        Entity target = Target(Start + new Vector3(0, 0, 9));
        Run(2);

        Assert.True(Cast("entangle", distance: 9).IsApplied);
        Run(26);

        Assert.Equal(target, _world.World.GetComponent<CompanionState>(bull).Foe);
        Assert.Equal((int)CompanionMode.Fight, _world.World.GetComponent<CompanionState>(bull).Mode);
        Assert.Equal(Emote.Affection, _world.World.GetComponent<CurrentEmote>(bull).Value);
        Assert.NotEqual((int)CompanionMode.Fight, _world.World.GetComponent<CompanionState>(learner).Mode);

        Run(90);
        Assert.True(_world.World.GetComponent<CreatureCombos>(bull).Landed0 > 5, "and it landed the combo");
        Assert.Equal(1, _systems.Fight.Combos.SelfStarted);
    }

    private IntentResult Cast(string spell, float distance)
    {
        int index = _world.Data.Spells.Select((definition, i) => (definition, i)).Single(item => item.definition.Id == spell).i;
        var transaction = new CastTransaction(_world, _alice, index, 0, distance, new StableId(0));
        return new TransactionProcessor(new IntentDeduplicator()).Execute(new IntentId(1, 1), ref transaction, _world.Clock.Current.Index);
    }

    private void Charge(Entity bull, Entity target)
    {
        Vector3 aim = _world.World.GetComponent<Position>(target).Value - _world.World.GetComponent<Position>(bull).Value;
        CombatRules.Start(_world.World, _world.Data, bull, _world.Data.MoveIndex("charge"), aim, _world.Clock.Current.Index);
    }

    private Entity Companion(Vector3 at, bool summoned)
    {
        Entity creature = _world.SpawnCreature(_world.SpeciesIndex("mossback"), at, new Personality(0, 100, 0, -100), group: 7);
        _world.Services.Bonding.Restore(
            _world.World.GetComponent<MindRef>(creature).Handle, new BondState(OwnershipState.Pacted, Alice, PactTerms.Of(0), 0, 3, summoned, 0, 0, false));
        _world.World.GetComponent<Combatant>(creature) = _world.World.GetComponent<Combatant>(creature) with { Power = 100 };
        return creature;
    }

    private Entity Target(Vector3 at)
    {
        Entity creature = _world.SpawnCreature(_world.SpeciesIndex("bogfox"), at, new Personality(100, 0, 0, -100), group: 8);
        _world.World.GetComponent<Combatant>(creature) = _world.World.GetComponent<Combatant>(creature) with { Health = 1000, MaxHealth = 1000 };
        return creature;
    }

    private int Health(Entity entity) => _world.World.GetComponent<Combatant>(entity).Health;

    private void Run(int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            _scheduler.RunTick();
        }
    }
}
