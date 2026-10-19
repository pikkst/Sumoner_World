namespace SumonerWorld.Core.Simulation;

/// <summary>
/// Converts real frame time, measured by the host, into a whole number of fixed ticks (TDD §2.5).
/// Uses integer arithmetic so no time is lost or gained over long sessions.
/// </summary>
public sealed class FixedStepAccumulator
{
    private const long NanosecondsPerSecond = 1_000_000_000L;

    private readonly int _ticksPerSecond;
    private readonly int _maxCatchUpTicks;

    // Pending time in units of (nanoseconds x ticks per second); one tick equals NanosecondsPerSecond units.
    private long _pendingUnits;

    /// <summary>Creates an accumulator for the given settings.</summary>
    public FixedStepAccumulator(SimulationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _ticksPerSecond = settings.TicksPerSecond;
        _maxCatchUpTicks = settings.MaxCatchUpTicks;
    }

    /// <summary>Total ticks dropped because a frame needed more than the catch-up limit (reported as a metric).</summary>
    public long DroppedTicks { get; private set; }

    /// <summary>Progress towards the next tick in [0, 1), for presentation interpolation only.</summary>
    public double Alpha => (double)_pendingUnits / NanosecondsPerSecond;

    /// <summary>Adds elapsed real time and returns how many ticks the host should run now.</summary>
    public int Accumulate(long elapsedNanoseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(elapsedNanoseconds);

        _pendingUnits += elapsedNanoseconds * _ticksPerSecond;
        long due = _pendingUnits / NanosecondsPerSecond;
        _pendingUnits -= due * NanosecondsPerSecond;

        if (due > _maxCatchUpTicks)
        {
            DroppedTicks += due - _maxCatchUpTicks;
            return _maxCatchUpTicks;
        }

        return (int)due;
    }
}
