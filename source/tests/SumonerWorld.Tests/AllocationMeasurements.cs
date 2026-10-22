namespace SumonerWorld.Tests;

/// <summary>
/// Tests that assert "0 bytes allocated" with <see cref="GC.GetAllocatedBytesForCurrentThread"/>. That counter can jump by up to one
/// allocation context (≈ 8 KB) when a GC started by another thread retires this thread's context, so these tests run alone, after
/// the parallel ones: then only their own allocations can trigger a GC.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AllocationMeasurements
{
    /// <summary>The collection name.</summary>
    public const string Name = "Allocation measurements";

    /// <summary>
    /// Bytes <paramref name="body"/> allocates on this thread: the fewest of up to <paramref name="runs"/> measurements after one
    /// warm-up run. The runtime can allocate a little on the thread while it recompiles a hot loop (tiered compilation, on-stack
    /// replacement), once and not per call, so one clean run is enough to show the code itself does not allocate.
    /// </summary>
    public static long Fewest(Action body, int runs = 3)
    {
        ArgumentNullException.ThrowIfNull(body);
        body();
        long fewest = long.MaxValue;
        for (int run = 0; run < runs && fewest != 0; run++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            body();
            fewest = Math.Min(fewest, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        return fewest;
    }
}
