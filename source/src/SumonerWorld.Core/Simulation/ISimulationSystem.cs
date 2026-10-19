namespace SumonerWorld.Core.Simulation;

/// <summary>
/// One unit of simulation work, run once per tick in its <see cref="Stage"/> (TDD §2.2).
/// Dependencies are passed to the constructor (R07); systems talk to each other only through events (R10).
/// </summary>
public interface ISimulationSystem
{
    /// <summary>The stage this system runs in.</summary>
    SimulationStage Stage { get; }

    /// <summary>Runs the system for one tick. Must not allocate or block in steady state (R21).</summary>
    void Execute(in SimulationTick tick);
}
