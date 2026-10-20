using SumonerWorld.Core.Randomness;

namespace SumonerWorld.Core.World;

/// <summary>Kinds of placeholder props scattered on the M1 test region.</summary>
public enum PropKind : byte
{
    /// <summary>A rock.</summary>
    Rock,

    /// <summary>A tree.</summary>
    Tree,

    /// <summary>A bush (shown only in the full ring).</summary>
    Bush,
}

/// <summary>One scattered prop, in chunk-local metres (0–64 on X and Z); the Game layer puts it on the terrain height.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="X">Position along X inside the chunk.</param>
/// <param name="Z">Position along Z inside the chunk.</param>
/// <param name="Yaw">Rotation around Y in radians.</param>
/// <param name="Scale">Uniform scale.</param>
public readonly record struct ScatteredProp(PropKind Kind, float X, float Z, float Yaw, float Scale);

/// <summary>
/// Seeded scatter of placeholder props per chunk (TDD §5.1: reproducible world content, R25). The same seed and chunk always give
/// the same props, in the same order, so any client and the server agree without sending them. The order puts the props the medium
/// ring shows (trees and rocks) first: the medium ring takes a prefix. The full M2 world generator replaces this.
/// </summary>
public static class ChunkScatter
{
    /// <summary>Trees per chunk.</summary>
    public const int Trees = 24;

    /// <summary>Rocks per chunk.</summary>
    public const int Rocks = 16;

    /// <summary>Bushes per chunk.</summary>
    public const int Bushes = 60;

    /// <summary>Props the medium ring shows (the first part of <see cref="Generate"/>'s output).</summary>
    public const int MediumCount = Trees + Rocks;

    /// <summary>Props per chunk.</summary>
    public const int Count = Trees + Rocks + Bushes;

    /// <summary>Writes the props of <paramref name="chunk"/> into <paramref name="props"/> (length ≥ <see cref="Count"/>).</summary>
    public static void Generate(ulong worldSeed, ChunkCoord chunk, Span<ScatteredProp> props)
    {
        if (props.Length < Count)
        {
            throw new ArgumentException($"Needs room for {Count} props.", nameof(props));
        }

        IRandom random = new SeededRandom(worldSeed).Fork(StreamOf(chunk));
        int i = 0;
        i = Fill(random, props, i, PropKind.Tree, Trees, 0.8f, 1.4f);
        i = Fill(random, props, i, PropKind.Rock, Rocks, 0.5f, 2.0f);
        Fill(random, props, i, PropKind.Bush, Bushes, 0.6f, 1.2f);
    }

    private static ulong StreamOf(ChunkCoord chunk) => ((ulong)(uint)chunk.X << 32) | (uint)chunk.Z;

    private static int Fill(IRandom random, Span<ScatteredProp> props, int start, PropKind kind, int count, float minScale, float maxScale)
    {
        for (int n = 0; n < count; n++)
        {
            props[start + n] = new ScatteredProp(
                kind,
                (float)(random.NextDouble() * ChunkCoord.Size),
                (float)(random.NextDouble() * ChunkCoord.Size),
                (float)(random.NextDouble() * Math.Tau),
                minScale + (float)(random.NextDouble() * (maxScale - minScale)));
        }

        return start + count;
    }
}
