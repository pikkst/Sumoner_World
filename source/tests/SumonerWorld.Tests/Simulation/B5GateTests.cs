using SumonerWorld.Scenarios.B5;

namespace SumonerWorld.Tests.Simulation;

/// <summary>
/// CI gates from M0-07 A3 on scenario B5 (sim-only): zero allocations per tick after warm-up, and identical runs
/// (determinism). Timings are not gated here because CI hardware varies; the benchmark tool reports them.
/// </summary>
public class B5GateTests
{
    private const ulong Seed = 0x5357_0001UL;

    [Fact]
    public void Steady_state_ticks_allocate_nothing()
    {
        var scenario = new B5Scenario(Seed);
        for (int i = 0; i < B5Scenario.WarmUpTicks; i++)
        {
            scenario.RunTick();
        }

        int growthsBefore = scenario.Events.CapacityGrowths;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < B5Scenario.MeasuredTicks; i++)
        {
            scenario.RunTick();
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated == 0, $"B5 allocated {allocated} bytes over {B5Scenario.MeasuredTicks} ticks (R21: must be 0).");
        Assert.Equal(growthsBefore, scenario.Events.CapacityGrowths);
        Assert.Equal(B5Scenario.CreatureCount, scenario.LiveCreatures);
        Assert.Equal(B5Scenario.CreatureCount, scenario.LiveMinds);
        Assert.True(scenario.StoredMemories > 0, "B5 must exercise the memory store (M1-06).");
    }

    [Fact]
    public void Two_runs_with_the_same_seed_match_on_every_tick()
    {
        ulong[] first = RunAndHash(Seed);
        ulong[] second = RunAndHash(Seed);

        int firstDifference = Enumerable.Range(0, first.Length).FirstOrDefault(tick => first[tick] != second[tick], -1);
        Assert.True(firstDifference < 0, $"Runs diverged at tick {firstDifference} (R11).");
    }

    [Fact]
    public void Different_seeds_produce_different_worlds()
    {
        Assert.NotEqual(RunAndHash(Seed)[^1], RunAndHash(Seed + 1)[^1]);
    }

    private static ulong[] RunAndHash(ulong seed)
    {
        var scenario = new B5Scenario(seed);
        var hashes = new ulong[B5Scenario.WarmUpTicks + B5Scenario.MeasuredTicks];
        for (int i = 0; i < hashes.Length; i++)
        {
            scenario.RunTick();
            hashes[i] = scenario.ComputeStateHash();
        }

        return hashes;
    }
}
