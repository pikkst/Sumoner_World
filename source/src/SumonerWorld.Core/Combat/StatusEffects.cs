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
}

/// <summary>
/// The status effects on a combatant (combat spec §7, M2-11): until when each lasts, the health a mend still has to give and the
/// slowed speed. They refresh, never stack. No padding (hashed raw).
/// </summary>
/// <param name="RootedUntil">Tick rooted ends (0 = not rooted).</param>
/// <param name="SlowedUntil">Tick slowed ends.</param>
/// <param name="MendingUntil">Tick mending ends.</param>
/// <param name="MendLeft">Health the mend still gives.</param>
/// <param name="SlowPercent">Speed while slowed, in percent.</param>
public record struct StatusEffects(ulong RootedUntil, ulong SlowedUntil, ulong MendingUntil, int MendLeft, int SlowPercent)
{
    /// <summary>The effects active at <paramref name="now"/>.</summary>
    public readonly StatusMarks At(ulong now) =>
        (now < RootedUntil ? StatusMarks.Rooted : 0) | (now < SlowedUntil ? StatusMarks.Slowed : 0) | (now < MendingUntil ? StatusMarks.Mending : 0);

    /// <summary>Movement speed now, in percent (0 when rooted).</summary>
    public readonly int SpeedPercent(ulong now) => now < RootedUntil ? 0 : now < SlowedUntil ? SlowPercent : 100;
}

/// <summary>Putting status effects on combatants (combat spec §7).</summary>
public static class StatusRules
{
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

    /// <summary>Puts effect <paramref name="id"/> on <paramref name="target"/> (refreshing it); a mend brings <paramref name="heal"/> health.</summary>
    public static void Apply(EcsWorld world, IDataRegistry data, Entity target, string id, ulong now, int heal = 0)
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
            default:
                status.MendingUntil = until;
                status.MendLeft = heal;
                break;
        }
    }
}

/// <summary>
/// Status effects over time (combat spec §7, stage 8): a mend gives its health spread over its remaining ticks; effects that
/// have all ended are removed.
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
            if (now < status.MendingUntil && status.MendLeft > 0 && _combatants.Has(entity))
            {
                ref Combatant combatant = ref _combatants.Get(entity);
                int heal = (int)Math.Max(1, (ulong)status.MendLeft / (status.MendingUntil - now));
                heal = Math.Min(heal, status.MendLeft);
                status.MendLeft -= heal;
                combatant.Health = combatant.Health == 0 ? 0 : Math.Min(combatant.MaxHealth, combatant.Health + heal);
            }

            if (status.At(now) == StatusMarks.None)
            {
                _world.Remove<StatusEffects>(entity);
            }
        }
    }
}
