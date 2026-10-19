using System.Diagnostics;
using System.Globalization;
using System.Text;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Scenarios.B5;

namespace SumonerWorld.Benchmarks.Scenarios;

/// <summary>
/// Runs scenario B5 (M0-07 A1) and writes one CSV row per measured tick: total and per-stage microseconds and bytes allocated
/// (M0-07 A2/A4). Prints p50/p99 for the tick and the busiest stages.
/// </summary>
internal static class B5Runner
{
    private const ulong Seed = 0x5357_0001UL;

    public static int Run(string outputPath)
    {
        var metrics = new StopwatchMetrics();
        using var scenario = new B5Scenario(Seed, metrics);
        for (int i = 0; i < B5Scenario.WarmUpTicks; i++)
        {
            scenario.RunTick();
        }

        int stageCount = StopwatchMetrics.StageCount;
        var tickMicroseconds = new double[B5Scenario.MeasuredTicks];
        var stageMicroseconds = new double[stageCount][];
        for (int s = 0; s < stageCount; s++)
        {
            stageMicroseconds[s] = new double[B5Scenario.MeasuredTicks];
        }

        var allocated = new long[B5Scenario.MeasuredTicks];
        for (int t = 0; t < B5Scenario.MeasuredTicks; t++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            scenario.RunTick();
            allocated[t] = GC.GetAllocatedBytesForCurrentThread() - before;
            tickMicroseconds[t] = ToMicroseconds(metrics.LastTickTicks);
            for (int s = 0; s < stageCount; s++)
            {
                stageMicroseconds[s][t] = ToMicroseconds(metrics.LastStageTicks[s]);
            }
        }

        WriteCsv(outputPath, tickMicroseconds, stageMicroseconds, allocated);
        PrintSummary(tickMicroseconds, stageMicroseconds, allocated);
        return 0;
    }

    private static void WriteCsv(string path, double[] ticks, double[][] stages, long[] allocated)
    {
        var csv = new StringBuilder();
        csv.Append("tick,tick_us");
        foreach (SimulationStage stage in Enum.GetValues<SimulationStage>())
        {
            csv.Append(CultureInfo.InvariantCulture, $",{stage}_us");
        }

        csv.AppendLine(",alloc_bytes");
        for (int t = 0; t < ticks.Length; t++)
        {
            csv.Append(CultureInfo.InvariantCulture, $"{B5Scenario.WarmUpTicks + t},{ticks[t]:F2}");
            foreach (double[] stage in stages)
            {
                csv.Append(CultureInfo.InvariantCulture, $",{stage[t]:F2}");
            }

            csv.AppendLine(CultureInfo.InvariantCulture, $",{allocated[t]}");
        }

        File.WriteAllText(path, csv.ToString());
        Console.WriteLine($"Wrote {ticks.Length} rows to {Path.GetFullPath(path)}");
    }

    private static void PrintSummary(double[] ticks, double[][] stages, long[] allocated)
    {
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"B5: {B5Scenario.CreatureCount} creatures, {ticks.Length} measured ticks after {B5Scenario.WarmUpTicks} warm-up"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  tick          p50 {Percentile(ticks, 50),8:F1} us   p99 {Percentile(ticks, 99),8:F1} us   max {ticks.Max(),8:F1} us"));
        foreach (SimulationStage stage in Enum.GetValues<SimulationStage>())
        {
            double[] values = stages[(int)stage];
            if (Percentile(values, 99) >= 1.0)
            {
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {stage,-13} p50 {Percentile(values, 50),8:F1} us   p99 {Percentile(values, 99),8:F1} us"));
            }
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  allocated     {allocated.Sum()} bytes total"));
    }

    private static double Percentile(double[] values, int percentile)
    {
        double[] sorted = [.. values.Order()];
        int index = (int)Math.Ceiling(percentile / 100.0 * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    private static double ToMicroseconds(long stopwatchTicks) => stopwatchTicks * 1_000_000.0 / Stopwatch.Frequency;
}
