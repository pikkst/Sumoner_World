namespace SumonerWorld.Core.Simulation;

/// <summary>
/// The world's only source of time (R11). Simulation code reads time from here, never from the wall clock.
/// </summary>
public interface ISimulationClock
{
    /// <summary>Time of the tick currently being simulated.</summary>
    SimulationTick Current { get; }

    /// <summary>Fixed simulation steps per real second.</summary>
    int TicksPerSecond { get; }

    /// <summary>Simulation ticks in one in-game day.</summary>
    int TicksPerDay { get; }
}
