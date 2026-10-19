namespace SumonerWorld.Core.Randomness;

/// <summary>
/// Deterministic random source for all simulation code (R11, R25).
/// Simulation systems receive an <see cref="IRandom"/> through injection and never create their own.
/// </summary>
public interface IRandom
{
    /// <summary>Returns the next raw 64-bit value.</summary>
    ulong NextUInt64();

    /// <summary>Returns an integer in the range [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>).</summary>
    int NextInt(int minInclusive, int maxExclusive);

    /// <summary>Returns a value in the range [0, 1).</summary>
    double NextDouble();

    /// <summary>Creates an independent stream derived from this one, e.g. one per creature or world chunk.</summary>
    IRandom Fork(ulong streamId);
}
