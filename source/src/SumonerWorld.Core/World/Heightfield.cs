using SumonerWorld.Core.Randomness;

namespace SumonerWorld.Core.World;

/// <summary>Settings of a generated <see cref="Heightfield"/>.</summary>
public sealed class HeightfieldSettings
{
    /// <summary>Samples per side (the M1 test region: 512 at 1 m spacing).</summary>
    public int Samples { get; init; } = 512;

    /// <summary>Metres between samples (Terrain3D vertex spacing).</summary>
    public float Spacing { get; init; } = 1f;

    /// <summary>Highest point above 0 m.</summary>
    public float Amplitude { get; init; } = 40f;

    /// <summary>Size of the largest hills, in metres.</summary>
    public float FeatureSize { get; init; } = 256f;

    /// <summary>Noise layers; each adds half the height at twice the detail.</summary>
    public int Octaves { get; init; } = 5;
}

/// <summary>
/// Terrain heights on a regular grid starting at the world origin (TDD §5.1). The M1 test region generates them from a seed
/// (fractal value noise), so the client terrain and, from M1-20, server movement use the same ground without shipping a heightmap.
/// Read-only after creation, so any thread may sample it. Hand-authored heightmaps replace the generator in M2.
/// </summary>
public sealed class Heightfield
{
    private const int LatticeSize = 256;

    private readonly float[] _heights;

    private Heightfield(float[] heights, int samples, float spacing)
    {
        _heights = heights;
        Samples = samples;
        Spacing = spacing;
    }

    /// <summary>Samples per side.</summary>
    public int Samples { get; }

    /// <summary>Metres between samples.</summary>
    public float Spacing { get; }

    /// <summary>Edge length in metres.</summary>
    public float Size => (Samples - 1) * Spacing;

    /// <summary>Heights row by row (Z rows of X samples).</summary>
    public ReadOnlySpan<float> Heights => _heights;

    /// <summary>Generates a field from a seed.</summary>
    public static Heightfield Generate(ulong seed, HeightfieldSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfLessThan(settings.Samples, 2);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(settings.Spacing);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(settings.Octaves);

        var random = new SeededRandom(seed);
        var lattice = new float[LatticeSize * LatticeSize];
        for (int i = 0; i < lattice.Length; i++)
        {
            lattice[i] = (float)random.NextDouble();
        }

        float norm = 0;
        for (int octave = 0, weight = 1; octave < settings.Octaves; octave++, weight *= 2)
        {
            norm += 1f / weight;
        }

        int n = settings.Samples;
        var heights = new float[n * n];
        for (int z = 0; z < n; z++)
        {
            for (int x = 0; x < n; x++)
            {
                float sum = 0;
                float frequency = settings.Spacing / settings.FeatureSize;
                float weight = 1;
                for (int octave = 0; octave < settings.Octaves; octave++)
                {
                    sum += weight * ValueNoise(lattice, x * frequency, z * frequency, octave);
                    frequency *= 2;
                    weight *= 0.5f;
                }

                heights[(z * n) + x] = sum / norm * settings.Amplitude;
            }
        }

        return new Heightfield(heights, n, settings.Spacing);
    }

    /// <summary>Height at a world position (bilinear between samples; clamped at the edges).</summary>
    public float HeightAt(float x, float z)
    {
        float fx = Math.Clamp(x / Spacing, 0, Samples - 1);
        float fz = Math.Clamp(z / Spacing, 0, Samples - 1);
        int x0 = Math.Min((int)fx, Samples - 2);
        int z0 = Math.Min((int)fz, Samples - 2);
        float tx = fx - x0;
        float tz = fz - z0;
        int row = z0 * Samples;
        float a = Lerp(_heights[row + x0], _heights[row + x0 + 1], tx);
        float b = Lerp(_heights[row + Samples + x0], _heights[row + Samples + x0 + 1], tx);
        return Lerp(a, b, tz);
    }

    private static float ValueNoise(float[] lattice, float x, float z, int octave)
    {
        // Each octave reads the lattice at an offset so layers do not line up.
        int offset = octave * 37;
        int xi = (int)MathF.Floor(x);
        int zi = (int)MathF.Floor(z);
        float tx = Smooth(x - xi);
        float tz = Smooth(z - zi);
        float a = Lerp(At(lattice, xi + offset, zi), At(lattice, xi + offset + 1, zi), tx);
        float b = Lerp(At(lattice, xi + offset, zi + 1), At(lattice, xi + offset + 1, zi + 1), tx);
        return Lerp(a, b, tz);
    }

    private static float At(float[] lattice, int x, int z) => lattice[((z & (LatticeSize - 1)) * LatticeSize) + (x & (LatticeSize - 1))];

    private static float Smooth(float t) => t * t * (3 - (2 * t));

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);
}
