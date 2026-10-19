using System.Diagnostics;
using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Core.Simulation;

namespace SumonerWorld.Benchmarks.Scenarios;

/// <summary>Times each tick and stage with <see cref="Stopwatch"/>; the harness reads the last tick's values after each tick.</summary>
internal sealed class StopwatchMetrics : IMetrics
{
    private readonly long[] _stageStarts = new long[StageCount];
    private long _tickStart;

    public static int StageCount { get; } = Enum.GetValues<SimulationStage>().Length;

    public long[] LastStageTicks { get; } = new long[StageCount];

    public long LastTickTicks { get; private set; }

    public void TickStarted(in SimulationTick tick) => _tickStart = Stopwatch.GetTimestamp();

    public void StageStarted(SimulationStage stage) => _stageStarts[(int)stage] = Stopwatch.GetTimestamp();

    public void StageCompleted(SimulationStage stage) =>
        LastStageTicks[(int)stage] = Stopwatch.GetTimestamp() - _stageStarts[(int)stage];

    public void TickCompleted(in SimulationTick tick) => LastTickTicks = Stopwatch.GetTimestamp() - _tickStart;
}
