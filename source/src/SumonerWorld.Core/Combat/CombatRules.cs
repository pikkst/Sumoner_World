using System.Numerics;
using SumonerWorld.Core.Data;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Intents;

namespace SumonerWorld.Core.Combat;

/// <summary>
/// A body that can fight and be hurt (combat spec §2), on players and creatures alike. Integers only (R11). Laid out without
/// padding so it can be hashed as raw bytes.
/// </summary>
/// <param name="LastHurtTick">Tick of the last damage taken (health regenerates after a delay).</param>
/// <param name="LastSpendTick">Tick of the last stamina spend (stamina regenerates after a delay).</param>
/// <param name="LastAttacker">Account of the player who hit it last; 0 when the last hit came from a creature or none.</param>
/// <param name="Health">Current health; 0 = defeated.</param>
/// <param name="MaxHealth">Full health.</param>
/// <param name="Stamina">Current stamina.</param>
/// <param name="MaxStamina">Full stamina.</param>
/// <param name="Mana">Current mana.</param>
/// <param name="MaxMana">Full mana.</param>
/// <param name="Power">Outgoing damage in percent.</param>
/// <param name="Guard">Flat damage reduction per hit.</param>
/// <param name="Poise">Stagger threshold (M2-02).</param>
/// <param name="ResistPhysical">Physical resistance, per mille.</param>
/// <param name="ResistPrimal">Primal resistance, per mille.</param>
/// <param name="Source">Species index whose moves it uses, or <see cref="CombatRules.PlayerSource"/>.</param>
public record struct Combatant(
    ulong LastHurtTick,
    ulong LastSpendTick,
    ulong LastAttacker,
    int Health,
    int MaxHealth,
    int Stamina,
    int MaxStamina,
    int Mana,
    int MaxMana,
    int Power,
    int Guard,
    int Poise,
    int ResistPhysical,
    int ResistPrimal,
    int Source);

/// <summary>A move in progress (combat spec §3): it hits during its active ticks and ends after recovery.</summary>
/// <param name="StartTick">Tick the move started.</param>
/// <param name="AimX">Aim direction, X (unit length with <paramref name="AimZ"/>).</param>
/// <param name="AimZ">Aim direction, Z.</param>
/// <param name="Move">Index into <see cref="IDataRegistry.Moves"/>.</param>
/// <param name="Hits">Targets hit so far (each at most once).</param>
/// <param name="Distance">Burst spells: how far ahead the circle's centre is, in metres (M2-11).</param>
/// <param name="Reserved">Padding, always 0.</param>
/// <param name="Target">Ward spells: the ally it is cast on (default = the caster).</param>
public record struct ActiveMove(ulong StartTick, float AimX, float AimZ, int Move, int Hits, float Distance = 0, int Reserved = 0, Entity Target = default);

/// <summary>A wild creature that gave up the fight (combat spec §6): it does not attack; left alone, it counts as spared.</summary>
/// <param name="Since">Tick it yielded.</param>
/// <param name="LastHurt">Tick it was last hurt.</param>
/// <param name="Account">The player it yielded to; 0 for a creature.</param>
public record struct Yielding(ulong Since, ulong LastHurt, ulong Account);

/// <summary>A defeated player, waiting to get up (combat spec §6; graves and shrines come later).</summary>
/// <param name="Since">Tick of the defeat.</param>
public record struct Downed(ulong Since);

/// <summary>A combatant holding its guard up (combat spec §5).</summary>
/// <param name="Since">Tick the block started (a hit soon after is a parry).</param>
public record struct Blocking(ulong Since);

/// <summary>A dodge (combat spec §5): no hit lands for the first ticks; the next dodge waits for the cooldown.</summary>
/// <param name="Since">Tick the dodge started.</param>
/// <param name="DirX">Dash direction, X.</param>
/// <param name="DirZ">Dash direction, Z.</param>
public record struct Dodging(ulong Since, float DirX, float DirZ);

/// <summary>Knocked off balance (poise break, parried, guard broken): cannot act until <paramref name="Until"/>.</summary>
/// <param name="Until">First tick it can act again.</param>
public record struct Staggered(ulong Until);

/// <summary>How a hit met its target.</summary>
public enum HitOutcome : byte
{
    /// <summary>Full damage.</summary>
    Hit,

    /// <summary>Blocked: reduced damage, paid with stamina.</summary>
    Blocked,

    /// <summary>Parried: no damage, the attacker is staggered.</summary>
    Parried,
}

/// <summary>What a combatant is doing, for animation and feedback (sent in snapshots). Values are part of the protocol.</summary>
public enum CombatAction : byte
{
    /// <summary>Nothing combat-related.</summary>
    None = 0,

    /// <summary>A move's wind-up (the telegraph).</summary>
    Windup = 1,

    /// <summary>A move's active part.</summary>
    Strike = 2,

    /// <summary>A move's recovery.</summary>
    Recover = 3,

    /// <summary>Blocking.</summary>
    Block = 4,

    /// <summary>Dodging (the invulnerable part).</summary>
    Dodge = 5,

    /// <summary>Staggered.</summary>
    Stagger = 6,

    /// <summary>A defeated player, down.</summary>
    Down = 7,

    /// <summary>A creature that yielded.</summary>
    Yield = 8,

    /// <summary>A player drawing a summoning sigil (since protocol 9, M2-06).</summary>
    Cast = 9,
}

/// <summary>Published for every hit that lands (feedback, telemetry, AI).</summary>
/// <param name="Attacker">Who hit.</param>
/// <param name="Target">Who was hit.</param>
/// <param name="Move">Move index.</param>
/// <param name="Damage">Health removed.</param>
/// <param name="HealthLeft">The target's health afterwards.</param>
/// <param name="Outcome">Hit, blocked or parried.</param>
public readonly record struct CombatHit(Entity Attacker, Entity Target, int Move, int Damage, int HealthLeft, HitOutcome Outcome = HitOutcome.Hit);

/// <summary>Published when a creature that yielded to a player was left alone and goes free (combat spec §6).</summary>
/// <param name="Creature">The spared creature.</param>
/// <param name="Account">The player who spared it.</param>
public readonly record struct CreatureSpared(Entity Creature, ulong Account);

/// <summary>Published when a combatant is defeated.</summary>
/// <param name="Target">The defeated entity (destroyed right after for wild creatures).</param>
/// <param name="Species">Its species index; <see cref="ushort.MaxValue"/> for a player.</param>
/// <param name="ByAccount">The player who defeated it; 0 for a creature or none.</param>
/// <param name="Witnesses">Kin that remembered the kill.</param>
public readonly record struct CombatantDefeated(Entity Target, ushort Species, ulong ByAccount, int Witnesses);

/// <summary>
/// The Combat contract v1.0.0 (combat spec, R28): one rulebook for players and creatures. The damage formula and the rules for
/// starting a move live here; the systems apply them each tick.
/// </summary>
public static class CombatRules
{
    /// <summary>Contract version; changes need a changelog entry in the combat spec (R28).</summary>
    public const string Version = "1.0.0";

    /// <summary><see cref="Combatant.Source"/> of a player.</summary>
    public const int PlayerSource = -1;

    private const int Percent = 100;
    private const int PerMille = 1000;

    /// <summary>
    /// Damage of one hit (combat spec §5): <c>max(1, damage × power / 100 × (1000 − resist) / 1000 − guard)</c>, in integers.
    /// </summary>
    public static int Damage(int moveDamage, int power, int resist, int guard) =>
        Math.Max(1, (int)((long)moveDamage * power * (PerMille - resist) / (Percent * PerMille)) - guard);

    /// <summary>A fresh combatant with full health, stamina and mana.</summary>
    public static Combatant Create(CombatStatsDefinition stats, int source)
    {
        ArgumentNullException.ThrowIfNull(stats);
        return new Combatant(
            0, 0, 0, stats.Health, stats.Health, stats.Stamina, stats.Stamina, stats.Mana, stats.Mana, stats.Power, stats.Guard, stats.Poise,
            stats.Resist.Physical, stats.Resist.Primal, source);
    }

    /// <summary>The resistance that applies to a damage type.</summary>
    public static int ResistTo(in Combatant target, DamageType type) => type switch
    {
        DamageType.Primal => target.ResistPrimal,
        _ => target.ResistPhysical,
    };

    /// <summary>The stats a combatant's moves come from.</summary>
    public static CombatStatsDefinition StatsOf(IDataRegistry data, in Combatant combatant)
    {
        ArgumentNullException.ThrowIfNull(data);
        return combatant.Source == PlayerSource ? data.Combat.Player : data.Species[combatant.Source].Combat;
    }

    /// <summary>
    /// Checks whether <paramref name="actor"/> may start <paramref name="move"/> now: it knows the move, is not already in a move
    /// (<see cref="IntentRejectReason.OnCooldown"/>), is not defeated or yielding, and has the stamina and mana (<paramref name="mana"/>
    /// overrides the move's cost, e.g. with resonance). Players know every spell (M2-11).
    /// </summary>
    public static IntentResult CanStart(EcsWorld world, IDataRegistry data, Entity actor, int move, int? mana = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(data);
        if (!world.Has<Combatant>(actor) || move < 0 || move >= data.Moves.Count)
        {
            return IntentResult.Reject(IntentRejectReason.InvalidArguments);
        }

        ref Combatant combatant = ref world.GetComponent<Combatant>(actor);
        bool playerSpell = data.Moves[move].IsSpell && combatant.Source == PlayerSource;
        if (!playerSpell && !Knows(StatsOf(data, combatant), data.Moves[move].Id) && !LearnedMove(world, data, actor, combatant.Source, data.Moves[move].Id))
        {
            return IntentResult.Reject(IntentRejectReason.InvalidArguments);
        }

        if (world.Has<ActiveMove>(actor))
        {
            return IntentResult.Reject(IntentRejectReason.OnCooldown);
        }

        if (world.Has<Bonding.SigilCast>(actor) || combatant.Health == 0 || world.Has<Yielding>(actor) || world.Has<Downed>(actor) || world.Has<Staggered>(actor) || world.Has<Dodging>(actor))
        {
            return IntentResult.Reject(IntentRejectReason.WrongState);
        }

        MoveDefinition definition = data.Moves[move];
        return combatant.Stamina < definition.Stamina || combatant.Mana < (mana ?? definition.Mana)
            ? IntentResult.Reject(IntentRejectReason.WrongState)
            : IntentResult.Applied();
    }

    /// <summary>
    /// Starts a move that <see cref="CanStart"/> allowed: pays its cost and aims it along <paramref name="aim"/> (ground plane). A
    /// burst spell lands <paramref name="distance"/> metres ahead; a ward spell is cast on <paramref name="target"/> (default: the
    /// caster).
    /// </summary>
    public static void Start(
        EcsWorld world, IDataRegistry data, Entity actor, int move, Vector3 aim, ulong now, float distance = 0, Entity target = default, int? mana = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(data);
        MoveDefinition definition = data.Moves[move];
        ref Combatant combatant = ref world.GetComponent<Combatant>(actor);
        combatant.Stamina -= definition.Stamina;
        combatant.Mana -= mana ?? definition.Mana;
        combatant.LastSpendTick = definition.Stamina > 0 ? now : combatant.LastSpendTick;
        var flat = new Vector2(aim.X, aim.Z);
        flat = flat.LengthSquared() > 0 ? Vector2.Normalize(flat) : Vector2.UnitY;
        world.Remove<Blocking>(actor);
        world.Add(actor, new ActiveMove(now, flat.X, flat.Y, move, 0, distance, 0, target));
    }

    /// <summary>Raises or lowers the guard (held button). Raising is refused while in a move, dodging, staggered or down.</summary>
    public static void SetBlock(EcsWorld world, Entity actor, bool raise, ulong now)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!raise)
        {
            world.Remove<Blocking>(actor);
            return;
        }

        if (!world.Has<Blocking>(actor) && CanDefend(world, actor) && !world.Has<ActiveMove>(actor) && !world.Has<Dodging>(actor))
        {
            world.Add(actor, new Blocking(now));
        }
    }

    /// <summary>
    /// Starts a dodge along <paramref name="direction"/> (ground plane) if allowed: not in a move, not staggered or down, the last
    /// dodge's cooldown over, and enough stamina. Returns whether it started.
    /// </summary>
    public static bool TryDodge(EcsWorld world, IDataRegistry data, Entity actor, Vector3 direction, ulong now)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(data);
        CombatDefinition rules = data.Combat;
        if (!CanDefend(world, actor) || world.Has<ActiveMove>(actor) || world.Has<Dodging>(actor))
        {
            return false;
        }

        ref Combatant combatant = ref world.GetComponent<Combatant>(actor);
        if (combatant.Stamina < rules.DodgeStamina)
        {
            return false;
        }

        combatant.Stamina -= rules.DodgeStamina;
        combatant.LastSpendTick = now;
        var flat = new Vector2(direction.X, direction.Z);
        flat = flat.LengthSquared() > 0 ? Vector2.Normalize(flat) : Vector2.UnitY;
        world.Remove<Blocking>(actor);
        world.Add(actor, new Dodging(now, flat.X, flat.Y));
        return true;
    }

    /// <summary>True during a dodge's invulnerable ticks.</summary>
    public static bool IsEvading(EcsWorld world, IDataRegistry data, Entity actor, ulong now)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(data);
        return world.Has<Dodging>(actor) && now - world.GetComponent<Dodging>(actor).Since < (ulong)data.Combat.DodgeTicks;
    }

    /// <summary>Knocks an entity off balance for the stagger time (refreshes a running stagger) and interrupts its move.</summary>
    public static void Stagger(EcsWorld world, IDataRegistry data, Entity actor, ulong now)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(data);
        var until = new Staggered(now + (ulong)data.Combat.StaggerTicks);
        world.Remove<Blocking>(actor);
        world.Remove<ActiveMove>(actor); // a stagger interrupts the action (combat spec §7); a spell's mana is spent
        if (world.Has<Staggered>(actor))
        {
            world.GetComponent<Staggered>(actor) = until;
        }
        else
        {
            world.Add(actor, until);
        }
    }

    /// <summary>What a combatant is doing now and which move (index, or −1), for animation and feedback.</summary>
    public static (CombatAction Action, int Move) ActionOf(EcsWorld world, IDataRegistry data, Entity entity, ulong now)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(data);
        if (world.Has<Downed>(entity))
        {
            return (CombatAction.Down, -1);
        }

        if (world.Has<Staggered>(entity))
        {
            return (CombatAction.Stagger, -1);
        }

        if (world.Has<Yielding>(entity))
        {
            return (CombatAction.Yield, -1);
        }

        if (IsEvading(world, data, entity, now))
        {
            return (CombatAction.Dodge, -1);
        }

        if (world.Has<Bonding.SigilCast>(entity))
        {
            return (CombatAction.Cast, -1);
        }

        if (world.Has<ActiveMove>(entity))
        {
            ActiveMove move = world.GetComponent<ActiveMove>(entity);
            MoveDefinition definition = data.Moves[move.Move];
            ulong age = now - move.StartTick;
            CombatAction phase = age < (ulong)definition.WindupTicks ? (definition.IsSpell ? CombatAction.Cast : CombatAction.Windup)
                : age < (ulong)(definition.WindupTicks + definition.ActiveTicks) ? CombatAction.Strike
                : CombatAction.Recover;
            return (phase, move.Move);
        }

        return world.Has<Blocking>(entity) ? (CombatAction.Block, -1) : (CombatAction.None, -1);
    }

    private static bool CanDefend(EcsWorld world, Entity actor) =>
        world.Has<Combatant>(actor) && world.GetComponent<Combatant>(actor).Health > 0
        && !world.Has<Downed>(actor) && !world.Has<Staggered>(actor) && !world.Has<Yielding>(actor);

    // A move a creature learned from its skill tree (M2-10).
    private static bool LearnedMove(EcsWorld world, IDataRegistry data, Entity actor, int source, string moveId)
    {
        if (!world.Has<Progression.CreatureSkills>(actor) || data.SkillTreeOf(source) is not SkillTreeDefinition tree)
        {
            return false;
        }

        ulong learned = world.GetComponent<Progression.CreatureSkills>(actor).Learned;
        for (int i = 0; i < tree.Nodes.Count; i++)
        {
            if ((learned & (1UL << i)) != 0 && tree.Nodes[i].Move == moveId)
            {
                return true;
            }
        }

        return false;
    }

    private static bool Knows(CombatStatsDefinition stats, string moveId)
    {
        for (int i = 0; i < stats.Moves.Count; i++)
        {
            if (stats.Moves[i] == moveId)
            {
                return true;
            }
        }

        return false;
    }
}
