using SumonerWorld.Core.Data;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Simulation;

namespace SumonerWorld.Core.Combat;

/// <summary>Status effects as bits (sent in snapshots since protocol 14). Values are part of the protocol.</summary>
[Flags]
public enum StatusMarks : byte
{
    /// <summary>None.</summary>
    None = 0,

    /// <summary>Cannot move (can still attack and cast).</summary>
    Rooted = 1,

    /// <summary>Moves slower.</summary>
    Slowed = 2,

    /// <summary>Heals over time.</summary>
    Mending = 4,

    /// <summary>Loses health over time (Ember, E3a).</summary>
    Burning = 8,

    /// <summary>Stamina comes back faster (Storm, E3a).</summary>
    Quickened = 16,

    /// <summary>Deals less damage (Umbral, E3a).</summary>
    Weakened = 32,

    /// <summary>Takes less damage (Umbral, E3a).</summary>
    Warded = 64,

    /// <summary>A shield takes damage before health (Ember, E3a).</summary>
    Shielded = 128,
}

/// <summary>
/// The status effects on a combatant (combat spec §7, M2-11, E3a): until when each lasts, the health a mend still has to give and
/// a burn still has to take, the shield left and the slowed speed. They refresh, never stack. No padding (hashed raw).
/// </summary>
/// <param name="RootedUntil">Tick rooted ends (0 = not rooted).</param>
/// <param name="SlowedUntil">Tick slowed ends.</param>
/// <param name="MendingUntil">Tick mending ends.</param>
/// <param name="MendLeft">Health the mend still gives.</param>
/// <param name="SlowPercent">Speed while slowed, in percent.</param>
/// <param name="BurningUntil">Tick burning ends.</param>
/// <param name="QuickenedUntil">Tick quickened ends.</param>
/// <param name="WeakenedUntil">Tick weakened ends.</param>
/// <param name="WardedUntil">Tick warded ends.</param>
/// <param name="ShieldedUntil">Tick the shield fades.</param>
/// <param name="BurnLeft">Health the burn still takes.</param>
/// <param name="ShieldLeft">Damage the shield still takes.</param>
public record struct StatusEffects(
    ulong RootedUntil,
    ulong SlowedUntil,
    ulong MendingUntil,
    int MendLeft,
    int SlowPercent,
    ulong BurningUntil = 0,
    ulong QuickenedUntil = 0,
    ulong WeakenedUntil = 0,
    ulong WardedUntil = 0,
    ulong ShieldedUntil = 0,
    int BurnLeft = 0,
    int ShieldLeft = 0)
{
    /// <summary>The effects active at <paramref name="now"/>.</summary>
    public readonly StatusMarks At(ulong now) =>
        (now < RootedUntil ? StatusMarks.Rooted : 0) | (now < SlowedUntil ? StatusMarks.Slowed : 0) | (now < MendingUntil ? StatusMarks.Mending : 0)
        | (now < BurningUntil ? StatusMarks.Burning : 0) | (now < QuickenedUntil ? StatusMarks.Quickened : 0) | (now < WeakenedUntil ? StatusMarks.Weakened : 0)
        | (now < WardedUntil ? StatusMarks.Warded : 0) | (now < ShieldedUntil && ShieldLeft > 0 ? StatusMarks.Shielded : 0);

    /// <summary>Movement speed now, in percent (0 when rooted).</summary>
    public readonly int SpeedPercent(ulong now) => now < RootedUntil ? 0 : now < SlowedUntil ? SlowPercent : 100;
}

/// <summary>Putting status effects on combatants (combat spec §7).</summary>
public static class StatusRules
{
    private const int Percent = 100;

    /// <summary>The flags of an entity at <paramref name="now"/> (none without effects).</summary>
    public static StatusMarks FlagsOf(EcsWorld world, Entity entity, ulong now)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.Has<StatusEffects>(entity) ? world.GetComponent<StatusEffects>(entity).At(now) : StatusMarks.None;
    }

    /// <summary>Movement speed of an entity now, in percent.</summary>
    public static int SpeedPercent(EcsWorld world, Entity entity, ulong now)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.Has<StatusEffects>(entity) ? world.GetComponent<StatusEffects>(entity).SpeedPercent(now) : 100;
    }

    /// <summary>
    /// Puts effect <paramref name="id"/> on <paramref name="target"/> (refreshing it); a mend brings <paramref name="heal"/> health,
    /// a shield takes <paramref name="shield"/> damage.
    /// </summary>
    public static void Apply(EcsWorld world, IDataRegistry data, Entity target, string id, ulong now, int heal = 0, int shield = 0)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(data);
        if (data.EffectOf(id) is not EffectDefinition effect)
        {
            return;
        }

        if (!world.Has<StatusEffects>(target))
        {
            world.Add(target, default(StatusEffects));
        }

        ref StatusEffects status = ref world.GetComponent<StatusEffects>(target);
        ulong until = now + (ulong)effect.Ticks;
        switch (id)
        {
            case "rooted":
                status.RootedUntil = until;
                break;
            case "slowed":
                status.SlowedUntil = until;
                status.SlowPercent = effect.SpeedPercent;
                break;
            case "burning":
                status.BurningUntil = until;
                status.BurnLeft = effect.Damage;
                break;
            case "quickened":
                status.QuickenedUntil = until;
                break;
            case "weakened":
                status.WeakenedUntil = until;
                break;
            case "warded":
                status.WardedUntil = until;
                break;
            case "shielded":
                status.ShieldedUntil = until;
                status.ShieldLeft = shield;
                break;
            default:
                status.MendingUntil = until;
                status.MendLeft = heal;
                break;
        }
    }

    /// <summary>
    /// The damage a hit does after the attacker's and the target's effects (E3a): Weakened attackers deal less, Warded targets take
    /// less, and a shield takes what it can before health. Uses up the shield.
    /// </summary>
    public static int Mitigate(EcsWorld world, IDataRegistry data, Entity attacker, Entity target, int damage, ulong now)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(data);
        if (damage <= 0)
        {
            return 0;
        }

        // Bit tests, not HasFlag: it boxes in debug builds, and this runs for every hit.
        StatusMarks attacking = FlagsOf(world, attacker, now);
        if ((attacking & StatusMarks.Weakened) != 0 && data.EffectOf("weakened") is EffectDefinition weakened)
        {
            damage = damage * weakened.DamagePercent / Percent;
        }

        StatusMarks defending = FlagsOf(world, target, now);
        if ((defending & StatusMarks.Warded) != 0 && data.EffectOf("warded") is EffectDefinition warded)
        {
            damage = damage * warded.DamagePercent / Percent;
        }

        if ((defending & StatusMarks.Shielded) != 0)
        {
            ref StatusEffects status = ref world.GetComponent<StatusEffects>(target);
            int absorbed = Math.Min(status.ShieldLeft, damage);
            status.ShieldLeft -= absorbed;
            damage -= absorbed;
        }

        return damage;
    }
}

/// <summary>
/// Status effects over time (combat spec §7, stage 8): a mend gives its health and a burn takes its health, each spread over its
/// remaining ticks; a burn never takes the last point of health of a creature that has yielded, and it counts as the burning
/// attacker's damage only through the hit that lit it. Effects that have all ended are removed.
/// </summary>
public sealed class StatusSystem : ISimulationSystem
{
    private readonly EcsWorld _world;
    private readonly ComponentStore<StatusEffects> _status;
    private readonly ComponentStore<Combatant> _combatants;

    /// <summary>Creates the system.</summary>
    public StatusSystem(EcsWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
        _status = world.Store<StatusEffects>();
        _combatants = world.Store<Combatant>();
    }

    /// <inheritdoc />
    public SimulationStage Stage => SimulationStage.StatusEffects;

    /// <inheritdoc />
    public void Execute(in SimulationTick tick)
    {
        ulong now = tick.Index;
        ReadOnlySpan<Entity> entities = _status.Entities;
        for (int i = entities.Length - 1; i >= 0; i--)
        {
            Entity entity = entities[i];
            ref StatusEffects status = ref _status.Components[i];
            if (_combatants.Has(entity))
            {
                ref Combatant combatant = ref _combatants.Get(entity);
                if (now < status.MendingUntil && status.MendLeft > 0)
                {
                    int heal = Spread(status.MendLeft, status.MendingUntil, now);
                    status.MendLeft -= heal;
                    combatant.Health = combatant.Health == 0 ? 0 : Math.Min(combatant.MaxHealth, combatant.Health + heal);
                }

                if (now < status.BurningUntil && status.BurnLeft > 0 && combatant.Health > 0)
                {
                    int burn = Spread(status.BurnLeft, status.BurningUntil, now);
                    status.BurnLeft -= burn;
                    combatant.Health = Math.Max(_world.Has<Yielding>(entity) ? 1 : 0, combatant.Health - burn);
                    combatant.LastHurtTick = now;
                }
            }

            if (status.At(now) == StatusMarks.None)
            {
                _world.Remove<StatusEffects>(entity);
            }
        }
    }

    // This tick's part of an amount spread evenly over the ticks left: what is kept is rounded up, so the whole amount is given by
    // the last tick and none of it early.
    private static int Spread(int left, ulong until, ulong now)
    {
        long remaining = (long)(until - now);
        long keep = (((long)left * (remaining - 1)) + remaining - 1) / remaining;
        return left - (int)keep;
    }
}
