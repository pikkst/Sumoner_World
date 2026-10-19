namespace SumonerWorld.Core.Simulation;

/// <summary>
/// Timing configuration of the fixed-step simulation (TDD §2.1). Loaded from server config, never hard-coded in systems (R48).
/// </summary>
public sealed class SimulationSettings
{
    /// <summary>Server tick rate from TDD §2.1.</summary>
    public const int DefaultTicksPerSecond = 30;

    /// <summary>One in-game day = 48 real minutes at 30 Hz (TDD §2.1).</summary>
    public const int DefaultTicksPerDay = 86_400;

    /// <summary>Most ticks a host may run in one frame before dropping time (prevents a catch-up spiral after a hitch).</summary>
    public const int DefaultMaxCatchUpTicks = 5;

    /// <summary>Creates validated settings.</summary>
    public SimulationSettings(int ticksPerSecond, int ticksPerDay, int maxCatchUpTicks)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ticksPerSecond);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ticksPerDay);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCatchUpTicks);

        TicksPerSecond = ticksPerSecond;
        TicksPerDay = ticksPerDay;
        MaxCatchUpTicks = maxCatchUpTicks;
    }

    /// <summary>Settings with the TDD v0.1 values.</summary>
    public static SimulationSettings Default { get; } =
        new(DefaultTicksPerSecond, DefaultTicksPerDay, DefaultMaxCatchUpTicks);

    /// <summary>Fixed simulation steps per real second.</summary>
    public int TicksPerSecond { get; }

    /// <summary>Simulation ticks in one in-game day.</summary>
    public int TicksPerDay { get; }

    /// <summary>Upper bound on ticks run per host frame.</summary>
    public int MaxCatchUpTicks { get; }
}
