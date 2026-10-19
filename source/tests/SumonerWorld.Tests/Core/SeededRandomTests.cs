using SumonerWorld.Core.Randomness;

namespace SumonerWorld.Tests.Core;

public class SeededRandomTests
{
    private const ulong Seed = 0xC0FFEEUL;
    private const int SampleCount = 1_000;

    [Fact]
    public void Same_seed_produces_identical_sequence()
    {
        var a = new SeededRandom(Seed);
        var b = new SeededRandom(Seed);

        for (int i = 0; i < SampleCount; i++)
        {
            Assert.Equal(a.NextUInt64(), b.NextUInt64());
        }
    }

    [Fact]
    public void Different_seeds_diverge()
    {
        var a = new SeededRandom(Seed);
        var b = new SeededRandom(Seed + 1);

        Assert.NotEqual(a.NextUInt64(), b.NextUInt64());
    }

    [Fact]
    public void Sequence_is_stable_across_builds()
    {
        // Golden values guard cross-platform/save determinism (R11, R25, R52).
        var random = new SeededRandom(0);

        Assert.Equal(0xE220A8397B1DCDAFUL, random.NextUInt64());
        Assert.Equal(0x6E789E6AA1B965F4UL, random.NextUInt64());
    }

    [Fact]
    public void NextInt_stays_within_bounds()
    {
        var random = new SeededRandom(Seed);

        for (int i = 0; i < SampleCount; i++)
        {
            int value = random.NextInt(-5, 5);
            Assert.InRange(value, -5, 4);
        }
    }

    [Fact]
    public void NextInt_rejects_empty_range()
    {
        var random = new SeededRandom(Seed);

        Assert.Throws<ArgumentOutOfRangeException>(() => random.NextInt(3, 3));
    }

    [Fact]
    public void NextDouble_is_in_unit_interval()
    {
        var random = new SeededRandom(Seed);

        for (int i = 0; i < SampleCount; i++)
        {
            Assert.InRange(random.NextDouble(), 0.0, 0.9999999999999999);
        }
    }

    [Fact]
    public void Fork_is_deterministic_and_independent_per_stream()
    {
        var forkA1 = new SeededRandom(Seed).Fork(1);
        var forkA2 = new SeededRandom(Seed).Fork(1);
        var forkB = new SeededRandom(Seed).Fork(2);

        ulong a1 = forkA1.NextUInt64();
        Assert.Equal(a1, forkA2.NextUInt64());
        Assert.NotEqual(a1, forkB.NextUInt64());
    }
}
