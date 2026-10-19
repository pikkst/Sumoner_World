using SumonerWorld.Core.Simulation;

namespace SumonerWorld.Core.Diagnostics;

/// <summary>
/// Receives tick and stage boundaries from <see cref="SimulationScheduler"/> (TDD §6). Implementations live outside Core and do
/// the timing themselves, so simulation code never reads a clock (R11). Calls must not allocate (R21).
/// </summary>
public interface IMetrics
{
    /// <summary>A tick is about to run.</summary>
    void TickStarted(in SimulationTick tick);

    /// <summary>A stage is about to run (called for every stage, including empty ones).</summary>
    void StageStarted(SimulationStage stage);

    /// <summary>A stage has finished.</summary>
    void StageCompleted(SimulationStage stage);

    /// <summary>The tick has finished, after events were cleared and before the clock advanced.</summary>
    void TickCompleted(in SimulationTick tick);
}
