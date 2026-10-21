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
public record struct ActiveMove(ulong StartTick, float AimX, float AimZ, int Move, int Hits);

/// <summary>A wild creature that gave up the fight (combat spec §6): it does not attack; left alone, it counts as spared.</summary>
/// <param name="Since">Tick it yielded.</param>
/// <param name="LastHurt">Tick it was last hurt.</param>
/// <param name="Account">The player it yielded to; 0 for a creature.</param>
public record struct Yielding(ulong Since, ulong LastHurt, ulong Account);

/// <summary>A defeated player, waiting to get up (combat spec §6; graves and shrines come later).</summary>
/// <param name="Since">Tick of the defeat.</param>
public record struct Downed(ulong Since);

/// <summary>Published for every hit that lands (feedback, telemetry, AI).</summary>
/// <param name="Attacker">Who hit.</param>
/// <param name="Target">Who was hit.</param>
/// <param name="Move">Move index.</param>
/// <param name="Damage">Health removed.</param>
/// <param name="HealthLeft">The target's health afterwards.</param>
public readonly record struct CombatHit(Entity Attacker, Entity Target, int Move, int Damage, int HealthLeft);

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
    /// (<see cref="IntentRejectReason.OnCooldown"/>), is not defeated or yielding, and has the stamina and mana.
    /// </summary>
    public static IntentResult CanStart(EcsWorld world, IDataRegistry data, Entity actor, int move)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(data);
        if (!world.Has<Combatant>(actor) || move < 0 || move >= data.Moves.Count)
        {
            return IntentResult.Reject(IntentRejectReason.InvalidArguments);
        }

        ref Combatant combatant = ref world.GetComponent<Combatant>(actor);
        if (!Knows(StatsOf(data, combatant), data.Moves[move].Id))
        {
            return IntentResult.Reject(IntentRejectReason.InvalidArguments);
        }

        if (world.Has<ActiveMove>(actor))
        {
            return IntentResult.Reject(IntentRejectReason.OnCooldown);
        }

        if (combatant.Health == 0 || world.Has<Yielding>(actor) || world.Has<Downed>(actor))
        {
            return IntentResult.Reject(IntentRejectReason.WrongState);
        }

        MoveDefinition definition = data.Moves[move];
        return combatant.Stamina < definition.Stamina || combatant.Mana < definition.Mana
            ? IntentResult.Reject(IntentRejectReason.WrongState)
            : IntentResult.Applied();
    }

    /// <summary>Starts a move that <see cref="CanStart"/> allowed: pays its cost and aims it along <paramref name="aim"/> (ground plane).</summary>
    public static void Start(EcsWorld world, IDataRegistry data, Entity actor, int move, Vector3 aim, ulong now)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(data);
        MoveDefinition definition = data.Moves[move];
        ref Combatant combatant = ref world.GetComponent<Combatant>(actor);
        combatant.Stamina -= definition.Stamina;
        combatant.Mana -= definition.Mana;
        combatant.LastSpendTick = definition.Stamina > 0 ? now : combatant.LastSpendTick;
        var flat = new Vector2(aim.X, aim.Z);
        flat = flat.LengthSquared() > 0 ? Vector2.Normalize(flat) : Vector2.UnitY;
        world.Add(actor, new ActiveMove(now, flat.X, flat.Y, move, 0));
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
