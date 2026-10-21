using System.Numerics;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.World;
using SumonerWorld.Scenarios;

namespace SumonerWorld.Tests.Core.Combat;

[Collection(AllocationMeasurements.Name)]
public class CombatTests
{
    private const ulong Alice = 1001;

    [Theory]
    [InlineData(8, 100, 0, 0, 8)]
    [InlineData(8, 100, 0, 2, 6)]
    [InlineData(20, 150, 0, 0, 30)]
    [InlineData(20, 100, 500, 0, 10)]
    [InlineData(12, 110, 100, 3, 8)]
    [InlineData(1, 50, 750, 10, 1)]
    public void Damage_follows_the_contract_formula_and_never_drops_below_one(int damage, int power, int resist, int guard, int expected) =>
        Assert.Equal(expected, CombatRules.Damage(damage, power, resist, guard));

    [Fact]
    public void Regeneration_shares_add_up_to_exactly_the_rate_every_second()
    {
        int total = 0;
        for (ulong tick = 1; tick <= 90; tick++)
        {
            total += VitalsSystem.Gain(20, tick, 30);
        }

        Assert.Equal(60, total);
        Assert.Equal(0, VitalsSystem.Gain(20, 0, 30));
    }

    [Theory]
    [InlineData(MoveShapeKind.Arc, 1.5f, 0f, true)]
    [InlineData(MoveShapeKind.Arc, -1.5f, 0f, false)]
    [InlineData(MoveShapeKind.Arc, 0f, 1.5f, false)]
    [InlineData(MoveShapeKind.Arc, 2.3f, 0f, true)]
    [InlineData(MoveShapeKind.Line, 5.3f, 0.3f, true)]
    [InlineData(MoveShapeKind.Line, 3f, 2f, false)]
    [InlineData(MoveShapeKind.Circle, -2.2f, 0f, true)]
    public void A_body_is_hit_only_inside_the_moves_shape(MoveShapeKind kind, float x, float z, bool hit)
    {
        // Attacker at the origin aiming along +X; the target's body radius is 0.4 m.
        var shape = new MoveShapeDefinition { Kind = kind, ReachCm = kind == MoveShapeKind.Line ? 500 : 200, AngleDeg = 90, WidthCm = 100 };

        Assert.Equal(hit, CombatSystem.InShape(shape, Vector3.Zero, Vector2.UnitX, new Vector3(x, 0, z), 0.4f));
    }

    [Fact]
    public void A_move_hits_after_its_windup_once_and_the_player_cannot_start_another_until_it_ends()
    {
        using var arena = new Arena();
        Entity fox = arena.Creature("bogfox", new Vector3(101.5f, 0, 100), boldness: 90);
        int light = arena.World.Data.MoveIndex("light");
        MoveDefinition move = arena.World.Data.Moves[light];

        arena.Swing(fox);
        Assert.Equal(IntentRejectReason.OnCooldown, CombatRules.CanStart(arena.World.World, arena.World.Data, arena.Avatar, light).Reason);
        arena.Run(move.WindupTicks);
        Assert.Equal(arena.MaxHealth(fox), arena.Health(fox));
        arena.Run(move.ActiveTicks + move.RecoveryTicks + 1); // the move ends on the tick its age reaches its length

        Assert.Equal(arena.MaxHealth(fox) - CombatRules.Damage(move.Damage, 100, 0, 0), arena.Health(fox));
        Assert.True(CombatRules.CanStart(arena.World.World, arena.World.Data, arena.Avatar, light).IsApplied);
        Assert.Contains(arena.MemoriesAbout(fox), memory => memory.Record.EventType == arena.Event("AttackedBy"));
    }

    [Fact]
    public void A_timid_creature_yields_when_hurt_and_is_spared_if_left_alone()
    {
        using var arena = new Arena();
        Entity fox = arena.Creature("bogfox", new Vector3(101.5f, 0, 100), boldness: -40);
        arena.World.Snare(fox, 0);

        arena.SwingUntil(fox, () => arena.World.World.Has<Yielding>(fox));
        Assert.True(arena.Health(fox) * 1000 < arena.MaxHealth(fox) * arena.World.Data.Combat.YieldBelowPerMille);
        Assert.Equal(IntentRejectReason.WrongState, CombatRules.CanStart(arena.World.World, arena.World.Data, fox, arena.World.Data.MoveIndex("bite")).Reason);

        arena.Run(arena.World.Data.Combat.SpareAfterTicks + 30);

        Assert.False(arena.World.World.Has<Yielding>(fox));
        Assert.Contains(arena.MemoriesAbout(fox), memory => memory.Record.EventType == arena.Event("DefeatedButSpared"));
    }

    [Fact]
    public void Hitting_a_yielded_creature_is_remembered_as_mistreatment_and_a_bold_one_never_yields()
    {
        using var arena = new Arena();
        Entity timid = arena.Creature("mossback", new Vector3(102.5f, 0, 100), boldness: -40); // enough health left to take the next hit
        arena.World.Snare(timid, 0);
        arena.SwingUntil(timid, () => arena.World.World.Has<Yielding>(timid));
        arena.SwingUntil(timid, () => arena.MemoriesAbout(timid).Any(memory => memory.Record.EventType == arena.Event("Mistreated")));

        using var other = new Arena();
        Entity bold = other.Creature("bogfox", new Vector3(101.5f, 0, 100), boldness: 90);
        other.World.Snare(bold, 0);
        StableId boldId = other.World.World.GetComponent<StableId>(bold);
        bool everYielded = false;
        for (int tick = 0; tick < 2_000 && other.World.TryFindCreature(boldId, out _); tick++)
        {
            everYielded |= other.World.World.Has<Yielding>(bold);
            other.TrySwing(bold);
            other.Run(1);
        }

        Assert.False(everYielded);
        Assert.False(other.World.TryFindCreature(boldId, out _));
    }

    [Fact]
    public void A_herd_does_not_hurt_its_own_and_a_defeated_player_gets_up_with_full_health()
    {
        using var arena = new Arena();
        Entity bull = arena.Creature("mossback", new Vector3(120, 0, 100), boldness: 90, group: 7);
        Entity calf = arena.Creature("mossback", new Vector3(122, 0, 100), boldness: 90, group: 7);
        int charge = arena.World.Data.MoveIndex("charge");
        CombatRules.Start(arena.World.World, arena.World.Data, bull, charge, Vector3.UnitX, arena.Now);
        arena.Run(60);
        Assert.Equal(arena.MaxHealth(calf), arena.Health(calf));

        arena.World.World.GetComponent<Combatant>(arena.Avatar) = arena.World.World.GetComponent<Combatant>(arena.Avatar) with { Health = 0 };
        arena.Run(1);
        Assert.True(arena.World.World.Has<Downed>(arena.Avatar));
        arena.Run(arena.World.Data.Combat.PlayerDownTicks + 1);

        Assert.False(arena.World.World.Has<Downed>(arena.Avatar));
        Assert.Equal(100, arena.Health(arena.Avatar));
    }

    [Fact]
    public void A_raised_guard_takes_a_share_of_the_damage_as_stamina_and_a_late_block_is_no_parry()
    {
        using var arena = new Arena();
        Entity fox = arena.Creature("bogfox", new Vector3(100, 0, 101.2f), boldness: 90);
        CombatRules.SetBlock(arena.World.World, arena.Avatar, raise: true, arena.Now);
        CombatRules.Start(arena.World.World, arena.World.Data, fox, arena.World.Data.MoveIndex("bite"), -Vector3.UnitZ, arena.Now);

        arena.Run(14);

        int full = CombatRules.Damage(7, 110, 0, 0);
        int blocked = full * arena.World.Data.Combat.BlockDamagePerMille / 1000;
        Assert.Equal(100 - blocked, arena.Health(arena.Avatar));
        Assert.Equal(100 - blocked, arena.World.World.GetComponent<Combatant>(arena.Avatar).Stamina);
        Assert.False(arena.World.World.Has<Staggered>(fox));
    }

    [Fact]
    public void A_block_just_before_the_hit_parries_and_staggers_the_attacker()
    {
        using var arena = new Arena();
        Entity fox = arena.Creature("bogfox", new Vector3(100, 0, 101.2f), boldness: 90);
        CombatRules.Start(arena.World.World, arena.World.Data, fox, arena.World.Data.MoveIndex("bite"), -Vector3.UnitZ, arena.Now);
        arena.Run(5);
        CombatRules.SetBlock(arena.World.World, arena.Avatar, raise: true, arena.Now);
        arena.Run(6);

        Assert.Equal(100, arena.Health(arena.Avatar));
        Assert.True(arena.World.World.Has<Staggered>(fox));
        Assert.Equal(CombatAction.Stagger, CombatRules.ActionOf(arena.World.World, arena.World.Data, fox, arena.Now).Action);
    }

    [Fact]
    public void A_dodge_lets_the_bite_pass_and_a_heavy_blow_staggers_a_light_body()
    {
        using var arena = new Arena();
        Entity fox = arena.Creature("bogfox", new Vector3(100, 0, 101.2f), boldness: 90);
        CombatRules.Start(arena.World.World, arena.World.Data, fox, arena.World.Data.MoveIndex("bite"), -Vector3.UnitZ, arena.Now);
        arena.Run(8);
        Assert.True(CombatRules.TryDodge(arena.World.World, arena.World.Data, arena.Avatar, Vector3.UnitX, arena.Now));
        arena.Run(20);
        Assert.Equal(100, arena.Health(arena.Avatar));

        var heavy = new AttackTransaction(arena.World, arena.Avatar, Slot: 1, Heading: 0); // heading 0 = +Z, at the fox
        Assert.True(heavy.Validate(arena.Now).IsApplied);
        heavy.Commit(arena.Now);
        arena.Run(5);
        (CombatAction action, int move) = CombatRules.ActionOf(arena.World.World, arena.World.Data, arena.Avatar, arena.Now);
        Assert.Equal((CombatAction.Windup, arena.World.Data.MoveIndex("heavy")), (action, move));
        arena.Run(17);

        Assert.True(arena.World.World.Has<Staggered>(fox));
        Assert.True(arena.Health(fox) < 60);
        Assert.Equal(IntentRejectReason.InvalidArguments, new AttackTransaction(arena.World, arena.Avatar, Slot: 5, Heading: 0).Validate(arena.Now).Reason);
    }

    [Fact]
    public void A_fight_is_deterministic_and_allocates_nothing_per_tick()
    {
        StateHash first = Fight(out long _);

        // A real per-tick allocation shows in every run. On a busy CI machine the runtime sometimes allocates once, lazily, when the
        // tick waits for a background job (timing-dependent, not per tick), so the measurement may be repeated: one clean run of
        // the same deterministic fight proves the tick itself allocates nothing.
        var measured = new List<long>();
        for (int run = 0; run < 3 && (measured.Count == 0 || measured[^1] != 0); run++)
        {
            StateHash again = Fight(out long allocated);
            Assert.Equal(first.Value, again.Value);
            measured.Add(allocated);
        }

        Assert.True(measured[^1] == 0, $"allocated per run: {string.Join(", ", measured)} bytes");
    }

    private static StateHash Fight(out long allocated)
    {
        using var arena = new Arena();
        var foxes = new List<Entity>();
        for (int i = 0; i < 6; i++)
        {
            foxes.Add(arena.Creature("bogfox", new Vector3(101.5f + (i % 2), 0, 99 + i), boldness: (i * 30) - 60));
        }

        arena.Run(60); // warm up: first-use paths may allocate
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int tick = 0; tick < 600; tick++)
        {
            foreach (Entity fox in foxes)
            {
                if (arena.World.World.IsAlive(fox))
                {
                    arena.TrySwing(fox);
                }
            }

            arena.Run(1);
        }

        allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var hash = new StateHash();
        arena.World.AddTo(ref hash, arena.World.Services.Bonding);
        return hash;
    }

    private sealed class Arena : IDisposable
    {
        private readonly SimulationScheduler _scheduler;

        public Arena()
        {
            World = new CreatureWorld(ShippedData.Current, new CreatureWorldSettings(), seed: 41);
            _scheduler = new SimulationScheduler(World.Clock, World.Events);
            CreatureSystems systems = World.CreateSystems();
            _scheduler.Register(systems.JobApply);
            _scheduler.Register(systems.Combat);
            _scheduler.Register(systems.Vitals);
            Avatar = World.SpawnPlayerAvatar(Alice, new Vector3(100, 0, 100));
        }

        public CreatureWorld World { get; }

        public Entity Avatar { get; }

        public ulong Now => World.Clock.Current.Index;

        public Entity Creature(string species, Vector3 at, int boldness, int group = 0) =>
            World.SpawnCreature(World.SpeciesIndex(species), at, new Personality((sbyte)boldness, 0, 0, 0), group);

        public int Health(Entity entity) => World.World.GetComponent<Combatant>(entity).Health;

        public int MaxHealth(Entity entity) => World.World.GetComponent<Combatant>(entity).MaxHealth;

        public ushort Event(string key) => World.Rules.FindByKey(key)!.Value;

        public RecalledMemory[] MemoriesAbout(Entity creature)
        {
            var memories = new RecalledMemory[16];
            int count = World.Minds.RecallAbout(World.World.GetComponent<MindRef>(creature).Handle, new MemorySubject(SubjectKind.Player, Alice), Now, memories);
            return memories[..count];
        }

        public void Swing(Entity target) => Assert.True(TrySwing(target), "the swing should start");

        public bool TrySwing(Entity target)
        {
            var hit = new HitTransaction(World, Avatar, World.World.GetComponent<StableId>(target));
            if (!hit.Validate(Now).IsApplied)
            {
                return false;
            }

            hit.Commit(Now);
            return true;
        }

        public void SwingUntil(Entity target, Func<bool> done)
        {
            for (int tick = 0; tick < 3_000 && !done(); tick++)
            {
                TrySwing(target);
                Run(1);
            }

            Assert.True(done(), "the fight should reach the expected state");
        }

        public void Run(int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                _scheduler.RunTick();
            }
        }

        public void Dispose() => World.Dispose();
    }
}
