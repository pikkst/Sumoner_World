namespace SumonerWorld.Core.Simulation;

/// <summary>Fixed-step world clock. Advanced only by <see cref="SimulationScheduler"/>.</summary>
public sealed class SimulationClock : ISimulationClock
{
    /// <summary>Creates a clock at <paramref name="startTick"/> (0 for a new world, the saved tick for a loaded one).</summary>
    public SimulationClock(SimulationSettings settings, ulong startTick = 0)
    {
        ArgumentNullException.ThrowIfNull(settings);

        TicksPerSecond = settings.TicksPerSecond;
        TicksPerDay = settings.TicksPerDay;
        Current = ToTick(startTick);
    }

    /// <inheritdoc />
    public SimulationTick Current { get; private set; }

    /// <inheritdoc />
    public int TicksPerSecond { get; }

    /// <inheritdoc />
    public int TicksPerDay { get; }

    /// <summary>Moves the clock to the next tick.</summary>
    public void Advance() => Current = ToTick(Current.Index + 1);

    private SimulationTick ToTick(ulong index)
    {
        ulong ticksPerDay = (ulong)TicksPerDay;
        return new SimulationTick(index, index / ticksPerDay, (int)(index % ticksPerDay));
    }
}
