using System.Diagnostics;
using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Net.Messages;

namespace SumonerWorld.Net.Server;

/// <summary>
/// Collects what the dev overlay shows (TDD §6) on a dev server: whole-tick and per-stage times and bytes allocated on the server
/// thread, as mean and maximum over a window that <see cref="TakeWindow"/> closes. Stage calls are passed on to <paramref name="next"/>
/// (the B2 benchmark, when one runs). Does not allocate while collecting (R21).
/// </summary>
/// <param name="next">Metrics that also receive the stage calls, if any.</param>
public sealed class ServerTickMetrics(IMetrics? next = null) : IMetrics
{
    private const double MicrosPerSecond = 1_000_000.0;

    private readonly long[] _stageStart = new long[ServerMetrics.StageCount];
    private readonly long[] _stageSum = new long[ServerMetrics.StageCount];
    private readonly long[] _stageMax = new long[ServerMetrics.StageCount];
    private long _tickStart;
    private long _tickSum;
    private long _tickMax;
    private long _allocatedStart;
    private long _allocatedSum;
    private int _ticks;

    /// <summary>Call right before the server tick.</summary>
    public void BeginServerTick()
    {
        _allocatedStart = GC.GetAllocatedBytesForCurrentThread();
        _tickStart = Stopwatch.GetTimestamp();
    }

    /// <summary>Call right after the server tick.</summary>
    public void EndServerTick()
    {
        long elapsed = Stopwatch.GetTimestamp() - _tickStart;
        _allocatedSum += GC.GetAllocatedBytesForCurrentThread() - _allocatedStart;
        _tickSum += elapsed;
        _tickMax = Math.Max(_tickMax, elapsed);
        _ticks++;
    }

    /// <inheritdoc />
    public void TickStarted(in SimulationTick tick) => next?.TickStarted(tick);

    /// <inheritdoc />
    public void StageStarted(SimulationStage stage)
    {
        next?.StageStarted(stage);
        _stageStart[(int)stage] = Stopwatch.GetTimestamp();
    }

    /// <inheritdoc />
    public void StageCompleted(SimulationStage stage)
    {
        long elapsed = Stopwatch.GetTimestamp() - _stageStart[(int)stage];
        _stageSum[(int)stage] += elapsed;
        _stageMax[(int)stage] = Math.Max(_stageMax[(int)stage], elapsed);
        next?.StageCompleted(stage);
    }

    /// <inheritdoc />
    public void TickCompleted(in SimulationTick tick) => next?.TickCompleted(tick);

    /// <summary>
    /// Ends the window: returns its tick count, whole-tick timing, stage timings and mean allocation per tick, and starts a new one.
    /// </summary>
    public (int Ticks, StageTiming Tick, StageTiming[] Stages, long AllocatedPerTick) TakeWindow()
    {
        int ticks = Math.Max(1, _ticks);
        var stages = new StageTiming[ServerMetrics.StageCount];
        for (int i = 0; i < stages.Length; i++)
        {
            stages[i] = new StageTiming(Micros(_stageSum[i] / ticks), Micros(_stageMax[i]));
        }

        var window = (_ticks, new StageTiming(Micros(_tickSum / ticks), Micros(_tickMax)), stages, _allocatedSum / ticks);
        Array.Clear(_stageSum);
        Array.Clear(_stageMax);
        _tickSum = 0;
        _tickMax = 0;
        _allocatedSum = 0;
        _ticks = 0;
        return window;
    }

    private static int Micros(long timestamps) => (int)Math.Min(int.MaxValue, timestamps * MicrosPerSecond / Stopwatch.Frequency);
}
