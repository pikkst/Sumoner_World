using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Randomness;

namespace SumonerWorld.Tests.Core.Cognition;

public class MemoryDecayTests
{
    private const uint HalfLife = 172_800; // 2 in-game days at 30 Hz

    [Fact]
    public void No_elapsed_time_or_no_half_life_means_no_decay()
    {
        Assert.Equal(900, MemoryDecay.Apply(900, 0, HalfLife));
        Assert.Equal(900, MemoryDecay.Apply(900, 10_000_000, 0));
    }

    [Theory]
    [InlineData(1, 500)]
    [InlineData(2, 250)]
    [InlineData(3, 125)]
    public void Each_whole_half_life_halves_the_strength(uint halfLives, int expected)
    {
        Assert.Equal(expected, MemoryDecay.Apply(1000, HalfLife * halfLives, HalfLife));
    }

    [Fact]
    public void Half_a_half_life_matches_the_exact_curve_within_one_point()
    {
        int exact = (int)Math.Floor(1000 * Math.Pow(2, -0.5));

        Assert.InRange(MemoryDecay.Apply(1000, HalfLife / 2, HalfLife), exact - 1, exact + 1);
    }

    [Fact]
    public void Sixteen_half_lives_or_more_forget_completely()
    {
        Assert.Equal(0, MemoryDecay.Apply(1000, HalfLife * 16, HalfLife));
        Assert.Equal(0, MemoryDecay.Apply(1000, uint.MaxValue, HalfLife));
    }

    [Fact]
    public void Strength_never_increases_and_stays_close_to_the_exact_curve()
    {
        // Property test over 10,000 seeded random times: monotonic, and within 1% of salience × 2^(−t/h).
        var random = new SeededRandom(11);
        var times = new uint[10_000];
        for (int i = 0; i < times.Length; i++)
        {
            times[i] = (uint)random.NextInt(0, (int)(HalfLife * 12));
        }

        Array.Sort(times);
        int previous = int.MaxValue;
        foreach (uint t in times)
        {
            int value = MemoryDecay.Apply(1000, t, HalfLife);
            double exact = 1000 * Math.Pow(2, -(double)t / HalfLife);

            Assert.True(value <= previous, $"Decay increased at t={t}.");
            Assert.InRange(value, exact - 10, exact + 1);
            previous = value;
        }
    }
}
