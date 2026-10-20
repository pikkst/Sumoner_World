namespace SumonerWorld.Core.Randomness;

/// <summary>
/// SplitMix64-based <see cref="IRandom"/>. The same seed yields the same sequence on every platform.
/// </summary>
public sealed class SeededRandom : IRandom
{
    private const ulong GoldenGamma = 0x9E3779B97F4A7C15UL;
    private const ulong MixMultiplierA = 0xBF58476D1CE4E5B9UL;
    private const ulong MixMultiplierB = 0x94D049BB133111EBUL;
    private const double UnitScale = 1.0 / (1UL << 53);

    private ulong _state;

    /// <summary>The generator state; <c>new SeededRandom(State)</c> continues exactly from here (saved with the world).</summary>
    public ulong State => _state;

    /// <summary>Creates a generator from a world or session seed.</summary>
    public SeededRandom(ulong seed)
    {
        _state = seed;
    }

    /// <inheritdoc />
    public ulong NextUInt64()
    {
        _state += GoldenGamma;
        return Mix(_state);
    }

    /// <inheritdoc />
    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxExclusive),
                maxExclusive,
                $"maxExclusive must be greater than minInclusive ({minInclusive}).");
        }

        ulong range = (ulong)((long)maxExclusive - minInclusive);
        return (int)((long)minInclusive + (long)(NextUInt64() % range));
    }

    /// <inheritdoc />
    public double NextDouble() => (NextUInt64() >> 11) * UnitScale;

    /// <inheritdoc />
    public IRandom Fork(ulong streamId) => new SeededRandom(Mix(_state ^ Mix(streamId + GoldenGamma)));

    private static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * MixMultiplierA;
        z = (z ^ (z >> 27)) * MixMultiplierB;
        return z ^ (z >> 31);
    }
}
