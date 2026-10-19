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
}
