using System.Numerics;
using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.Transactions;
using SumonerWorld.Core.World;
using SumonerWorld.Scenarios;

namespace SumonerWorld.Tests.Core.Combat;

public sealed class MagicTests : IDisposable
{
    private const ulong Alice = 1001;
    private static readonly Vector3 Start = new(100, 0, 100);

    private readonly CreatureWorld _world = new(ShippedData.Current, new CreatureWorldSettings(), seed: 41);
    private readonly SimulationScheduler _scheduler;
    private readonly TransactionProcessor _processor = new(new IntentDeduplicator());
    private readonly Entity _alice;
    private uint _sequence;

    public MagicTests()
    {
        _scheduler = new SimulationScheduler(_world.Clock, _world.Events);
        _world.CreateSystems().RegisterAll(_scheduler);
        _alice = _world.SpawnPlayerAvatar(Alice, Start);
    }

    public void Dispose() => _world.Dispose();

    [Fact]
    public void Spells_are_composed_from_their_runes_into_moves()
    {
        MoveDefinition thornbolt = _world.Data.Moves[_world.Data.MoveIndex("thornbolt")];
        MoveDefinition entangle = _world.Data.Moves[_world.Data.MoveIndex("entangle")];
        MoveDefinition mend = _world.Data.Moves[_world.Data.MoveIndex("mend")];

        Assert.True(thornbolt.IsSpell);
        Assert.Equal((12, MoveShapeKind.Line, 2000, 12, DamageType.Primal, "slowed", 10), (thornbolt.WindupTicks, thornbolt.Shape.Kind, thornbolt.Shape.ReachCm, thornbolt.Damage, thornbolt.DamageType, thornbolt.Effect, thornbolt.Mana));
        Assert.Equal((MoveShapeKind.Burst, 300, 0, "rooted", 20), (entangle.Shape.Kind, entangle.Shape.RadiusCm, entangle.Damage, entangle.Effect, entangle.Mana));
        Assert.Equal((MoveShapeKind.Ward, 30, "mending", 15), (mend.Shape.Kind, mend.Heal, mend.Effect, mend.Mana));
        Assert.Equal(["entangle", "mend", "thornbolt"], _world.Data.Spells.Select(spell => spell.Id));
    }

    [Fact]
    public void A_thornbolt_is_telegraphed_then_hurts_and_slows_what_it_hits_and_costs_mana()
    {
        Entity fox = Wild("bogfox", Start + new Vector3(0, 0, 10));
        int health = Health(fox);
        int mana = Mana(_alice);

        Assert.True(Cast("thornbolt", heading: 0).IsApplied);
        Assert.Equal(CombatAction.Cast, CombatRules.ActionOf(_world.World, _world.Data, _alice, _world.Clock.Current.Index).Action);
        Run(14);

        Assert.True(Health(fox) < health, "it was hit");
        Assert.True(_world.World.GetComponent<StatusEffects>(fox).At(_world.Clock.Current.Index).HasFlag(StatusMarks.Slowed));
        Assert.True(Mana(_alice) < mana, "the bolt cost mana");
    }

    [Fact]
    public void Entangle_roots_every_creature_in_its_circle_and_harms_none_and_rooted_creatures_stand_still()
    {
        Entity inside = Wild("mossback", Start + new Vector3(0, 0, 8));
        Entity alsoInside = Wild("bogfox", Start + new Vector3(2.8f, 0, 8.5f));
        Entity outside = Wild("bogfox", Start + new Vector3(0, 0, 18));
        int health = Health(inside);

        Assert.True(Cast("entangle", heading: 0, distance: 8).IsApplied);
        Run(26);
        ulong now = _world.Clock.Current.Index;

        Assert.True(StatusRules.FlagsOf(_world.World, inside, now).HasFlag(StatusMarks.Rooted));
        Assert.True(StatusRules.FlagsOf(_world.World, alsoInside, now).HasFlag(StatusMarks.Rooted));
        Assert.False(StatusRules.FlagsOf(_world.World, outside, now).HasFlag(StatusMarks.Rooted));
        Assert.Equal(health, Health(inside));

        Vector3 before = Position(inside);
        _world.World.GetComponent<MoveTarget>(inside) = new MoveTarget(before + new Vector3(10, 0, 0), 0.2f, 1);
        Run(20);
        Assert.Equal(before, Position(inside));
    }

    [Fact]
    public void Mend_heals_a_companion_over_four_seconds_or_the_caster_when_it_names_no_companion_of_theirs()
    {
        Entity companion = Companion("mossback", Start + new Vector3(3, 0, 0));
        ref Combatant hurt = ref _world.World.GetComponent<Combatant>(companion);
        hurt.Health = hurt.MaxHealth - 40;
        hurt.LastHurtTick = 10_000_000; // no natural regeneration during the test
        int before = Health(companion);

        Assert.True(Cast("mend", heading: 0, target: companion).IsApplied);
        Run(31 + 125);
        Assert.Equal(before + 30, Health(companion));

        ref Combatant self = ref _world.World.GetComponent<Combatant>(_alice);
        self.Health = 50;
        self.LastHurtTick = 10_000_000;
        Entity stranger = Wild("bogfox", Start + new Vector3(4, 0, 0));
        Assert.True(Cast("mend", heading: 0, target: stranger).IsApplied);
        Run(31 + 125);
        Assert.Equal(80, Health(_alice));
    }

    [Fact]
    public void A_summoned_Primal_companion_nearby_makes_spells_cheaper()
    {
        Companion("bogfox", Start + new Vector3(3, 0, 0));
        int mana = Mana(_alice);

        Assert.True(Cast("thornbolt", heading: 128).IsApplied);

        Assert.Equal(mana - 9, Mana(_alice));
    }

    [Fact]
    public void A_bolt_passes_the_casters_own_companion_and_hits_the_creature_behind_it()
    {
        Entity companion = Companion("bogfox", Start + new Vector3(0, 0, 4));
        Entity fox = Wild("bogfox", Start + new Vector3(0, 0, 9));
        int companionHealth = Health(companion);
        int foxHealth = Health(fox);

        Cast("thornbolt", heading: 0);
        Run(14);

        Assert.Equal(companionHealth, Health(companion));
        Assert.True(Health(fox) < foxHealth);
    }

    [Fact]
    public void Casting_needs_mana_and_a_stagger_breaks_the_cast_with_the_mana_spent()
    {
        _world.World.GetComponent<Combatant>(_alice) = _world.World.GetComponent<Combatant>(_alice) with { Mana = 5 };
        Assert.Equal(IntentRejectReason.WrongState, Cast("thornbolt", heading: 0).Reason);

        _world.World.GetComponent<Combatant>(_alice) = _world.World.GetComponent<Combatant>(_alice) with { Mana = 60 };
        Entity fox = Wild("bogfox", Start + new Vector3(0, 0, 10));
        int health = Health(fox);
        Assert.True(Cast("entangle", heading: 0, distance: 10).IsApplied);
        Run(5);
        CombatRules.Stagger(_world.World, _world.Data, _alice, _world.Clock.Current.Index);
        Run(30);

        Assert.False(StatusRules.FlagsOf(_world.World, fox, _world.Clock.Current.Index).HasFlag(StatusMarks.Rooted));
        Assert.True(Mana(_alice) < 60 - 15, "the mana was spent");
        Assert.Equal(health, Health(fox));
    }

    private IntentResult Cast(string spell, byte heading, float distance = 0, Entity target = default)
    {
        int index = _world.Data.Spells.Select((definition, i) => (definition, i)).Single(item => item.definition.Id == spell).i;
        StableId id = target != default && _world.World.Has<StableId>(target) ? _world.World.GetComponent<StableId>(target) : new StableId(0);
        var transaction = new CastTransaction(_world, _alice, index, heading, distance, id);
        return _processor.Execute(new IntentId(1, ++_sequence), ref transaction, _world.Clock.Current.Index);
    }

    private Entity Wild(string species, Vector3 at) => _world.SpawnCreature(_world.SpeciesIndex(species), at, new Personality(100, 0, 0, -100), group: 5);

    private Entity Companion(string species, Vector3 at)
    {
        Entity creature = _world.SpawnCreature(_world.SpeciesIndex(species), at, new Personality(0, 100, 0, -100), group: 6);
        _world.Services.Bonding.Restore(
            _world.World.GetComponent<MindRef>(creature).Handle, new BondState(OwnershipState.Pacted, Alice, PactTerms.Of(0), 0, 1, true, 0, 0, false));
        return creature;
    }

    private int Health(Entity entity) => _world.World.GetComponent<Combatant>(entity).Health;

    private int Mana(Entity entity) => _world.World.GetComponent<Combatant>(entity).Mana;

    private Vector3 Position(Entity entity) => _world.World.GetComponent<Position>(entity).Value;

    private void Run(int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            _scheduler.RunTick();
        }
    }
}
