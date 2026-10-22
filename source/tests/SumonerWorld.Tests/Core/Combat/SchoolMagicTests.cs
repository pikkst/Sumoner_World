using System.Numerics;
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

namespace SumonerWorld.Tests.Core.Combat;

/// <summary>The Storm, Umbral and Ember schools (combat spec §8a, E3a).</summary>
public sealed class SchoolMagicTests : IDisposable
{
    private const ulong Alice = 1001;
    private static readonly Vector3 Start = new(100, 0, 100);

    private readonly CreatureWorld _world = new(ShippedData.Current, new CreatureWorldSettings(), seed: 43);
    private readonly SimulationScheduler _scheduler;
    private readonly TransactionProcessor _processor = new(new IntentDeduplicator());
    private readonly Entity _alice;
    private uint _sequence;

    public SchoolMagicTests()
    {
        _scheduler = new SimulationScheduler(_world.Clock, _world.Events);
        _world.CreateSystems().RegisterAll(_scheduler);
        _alice = _world.SpawnPlayerAvatar(Alice, Start);
    }

    public void Dispose() => _world.Dispose();

    [Fact]
    public void Every_school_has_a_bolt_a_burst_and_a_ward()
    {
        foreach (string school in SumonerWorld.Core.Data.DataRegistryLoader.KnownSchools)
        {
            var shapes = _world.Data.Moves.Where(move => move.IsSpell && move.School == school).Select(move => move.Shape.Kind).ToHashSet();
            Assert.Equal(3, _world.Data.Moves.Count(move => move.IsSpell && move.School == school));
            Assert.Equal([MoveShapeKind.Line, MoveShapeKind.Burst, MoveShapeKind.Ward], shapes.Order());
        }

        Assert.Equal("storm", Schools.Of(SummonerClass.Human));
        Assert.Equal("umbral", Schools.Of(SummonerClass.Necromancer));
        Assert.Equal("ember", Schools.Of(SummonerClass.Dwarf));
    }

    [Fact]
    public void A_summoner_casts_only_the_spells_of_their_own_school()
    {
        Assert.Equal(IntentRejectReason.WrongState, Cast("spark", heading: 0).Reason);
        Assert.True(Cast("thornbolt", heading: 0).IsApplied);

        Become(SummonerClass.Human);
        Run(30);
        Assert.Equal(IntentRejectReason.WrongState, Cast("thornbolt", heading: 0).Reason);
        Assert.True(Cast("spark", heading: 0).IsApplied);
    }

    [Fact]
    public void A_spark_staggers_a_light_creature_but_not_a_heavy_one()
    {
        Become(SummonerClass.Human);
        Entity fox = Wild("bogfox", Start + new Vector3(0, 0, 8));
        Entity mossback = Wild("mossback", Start + new Vector3(0, 0, -8));
        int foxHealth = Health(fox);

        Assert.True(Cast("spark", heading: 0).IsApplied);
        Run(10);
        Assert.True(Health(fox) < foxHealth);
        Assert.True(_world.World.Has<Staggered>(fox), "poise 40 beats the bogfox's 15");

        Run(20);
        Assert.True(Cast("spark", heading: 128).IsApplied);
        Run(10);
        Assert.False(_world.World.Has<Staggered>(mossback), "the mossback's poise of 50 holds");
    }

    [Fact]
    public void A_soulbolt_heals_the_caster_by_half_of_what_it_took()
    {
        Become(SummonerClass.Necromancer);
        Entity fox = Wild("bogfox", Start + new Vector3(0, 0, 8));
        ref Combatant self = ref _world.World.GetComponent<Combatant>(_alice);
        self.Health = 50;
        self.LastHurtTick = 10_000_000;
        int foxHealth = Health(fox);

        Assert.True(Cast("soulbolt", heading: 0).IsApplied);
        Run(14);

        int took = foxHealth - Health(fox);
        Assert.True(took > 0);
        Assert.Equal(50 + (took / 2), Health(_alice));
    }

    [Fact]
    public void Weakened_deals_less_warded_takes_less_and_a_shield_takes_damage_first()
    {
        Entity fox = Wild("bogfox", Start + new Vector3(0, 0, 8));
        ulong now = _world.Clock.Current.Index;
        Assert.Equal(20, StatusRules.Mitigate(_world.World, _world.Data, fox, _alice, 20, now));

        StatusRules.Apply(_world.World, _world.Data, fox, "weakened", now);
        Assert.Equal(12, StatusRules.Mitigate(_world.World, _world.Data, fox, _alice, 20, now));

        StatusRules.Apply(_world.World, _world.Data, _alice, "warded", now);
        Assert.Equal(7, StatusRules.Mitigate(_world.World, _world.Data, fox, _alice, 20, now));

        StatusRules.Apply(_world.World, _world.Data, _alice, "shielded", now, shield: 10);
        Assert.Equal(0, StatusRules.Mitigate(_world.World, _world.Data, fox, _alice, 20, now));
        Assert.Equal(4, StatusRules.Mitigate(_world.World, _world.Data, fox, _alice, 20, now));
        Assert.False(StatusRules.FlagsOf(_world.World, _alice, now).HasFlag(StatusMarks.Shielded), "the shield is used up");
    }

    [Fact]
    public void Ember_shield_and_veil_go_on_the_caster_and_shade_grip_weakens_what_it_reaches()
    {
        Become(SummonerClass.Dwarf);
        Assert.True(Cast("ember_shield", heading: 0).IsApplied);
        Run(20);
        Assert.Equal(40, _world.World.GetComponent<StatusEffects>(_alice).ShieldLeft);

        Become(SummonerClass.Necromancer);
        Run(10);
        Assert.True(Cast("veil", heading: 0).IsApplied);
        Run(20);
        Entity fox = Wild("bogfox", Start + new Vector3(0, 0, 8));
        Run(10);
        Assert.True(Cast("shade_grip", heading: 0, distance: 8).IsApplied);
        Run(22);
        ulong now = _world.Clock.Current.Index;
        Assert.True(StatusRules.FlagsOf(_world.World, _alice, now).HasFlag(StatusMarks.Warded));
        Assert.True(StatusRules.FlagsOf(_world.World, fox, now).HasFlag(StatusMarks.Weakened));
    }

    [Fact]
    public void A_firebolt_keeps_burning_for_four_seconds_after_the_hit()
    {
        Become(SummonerClass.Dwarf);
        Entity mossback = Wild("mossback", Start + new Vector3(0, 0, 8));
        _world.World.GetComponent<Combatant>(mossback) = _world.World.GetComponent<Combatant>(mossback) with { LastHurtTick = 10_000_000 };

        Assert.True(Cast("firebolt", heading: 0).IsApplied);
        Run(14);
        int afterHit = Health(mossback);
        Assert.True(StatusRules.FlagsOf(_world.World, mossback, _world.Clock.Current.Index).HasFlag(StatusMarks.Burning));

        Run(125);
        Assert.Equal(afterHit - 18, Health(mossback));
        Assert.False(StatusRules.FlagsOf(_world.World, mossback, _world.Clock.Current.Index).HasFlag(StatusMarks.Burning));
    }

    [Fact]
    public void Tailwind_brings_stamina_back_three_times_as_fast()
    {
        Become(SummonerClass.Human);
        int normal = StaminaAfterRest();

        Assert.True(Cast("tailwind", heading: 0).IsApplied);
        Run(12);
        int quickened = StaminaAfterRest();

        Assert.InRange(quickened, (normal * 3) - 2, (normal * 3) + 2);
    }

    // Stamina regained in one second from 10 after the regeneration delay.
    private int StaminaAfterRest()
    {
        ref Combatant self = ref _world.World.GetComponent<Combatant>(_alice);
        ulong now = _world.Clock.Current.Index;
        self.Stamina = 10;
        self.LastSpendTick = now - (ulong)_world.Data.Combat.StaminaRegenDelayTicks;
        Run(30);
        return _world.World.GetComponent<Combatant>(_alice).Stamina - 10;
    }

    private void Become(SummonerClass summonerClass) =>
        _world.World.GetComponent<PlayerProgress>(_alice) = _world.World.GetComponent<PlayerProgress>(_alice) with { Class = (int)summonerClass };

    private IntentResult Cast(string spell, byte heading, float distance = 0)
    {
        int index = _world.Data.Spells.Select((definition, i) => (definition, i)).Single(item => item.definition.Id == spell).i;
        var transaction = new CastTransaction(_world, _alice, index, heading, distance, new StableId(0));
        return _processor.Execute(new IntentId(1, ++_sequence), ref transaction, _world.Clock.Current.Index);
    }

    private Entity Wild(string species, Vector3 at) => _world.SpawnCreature(_world.SpeciesIndex(species), at, new Personality(100, 0, 0, -100), group: 5);

    private int Health(Entity entity) => _world.World.GetComponent<Combatant>(entity).Health;

    private void Run(int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            _scheduler.RunTick();
        }
    }
}
