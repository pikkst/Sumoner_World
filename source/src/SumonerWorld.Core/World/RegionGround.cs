using SumonerWorld.Core.Data.Definitions;

namespace SumonerWorld.Core.World;

/// <summary>
/// Shapes a generated ground into the region (E4a): each biome scales the hills and raises or lowers the ground inside its circle,
/// fully in its inner two thirds and fading out over the outer third, and mountains rise along the region's edge so nobody walks
/// off the world. Biomes that overlap share by weight. The server, the client's prediction and the client's terrain all shape the
/// same way, so they stand on identical heights.
/// </summary>
public static class RegionGround
{
    /// <summary>How far in from the edge the mountains rise, in metres.</summary>
    public const float RimWidth = 80f;

    /// <summary>How high the mountains at the very edge are, in metres.</summary>
    public const float RimHeight = 45f;

    private const float FadeShare = 0.35f;

    /// <summary>Shapes <paramref name="field"/> in place.</summary>
    public static void Shape(Heightfield field, RegionDefinition region)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(region);
        Span<float> heights = field.MutableHeights;
        int n = field.Samples;
        for (int j = 0; j < n; j++)
        {
            for (int i = 0; i < n; i++)
            {
                float x = i * field.Spacing;
                float z = j * field.Spacing;
                (float scale, float offset) = Blend(region, x, z);
                int at = (j * n) + i;
                heights[at] = (heights[at] * scale) + offset + Rim(x, z, field.Size);
            }
        }
    }

    /// <summary>How much a biome counts at a point: 1 in its inner part, fading to 0 at its edge.</summary>
    public static float Weight(BiomeDefinition biome, float x, float z)
    {
        ArgumentNullException.ThrowIfNull(biome);
        float distance = MathF.Sqrt(((x - biome.X) * (x - biome.X)) + ((z - biome.Z) * (z - biome.Z)));
        float t = Math.Clamp((biome.Radius - distance) / (biome.Radius * FadeShare), 0, 1);
        return t * t * (3 - (2 * t));
    }

    /// <summary>The biome that counts most at a point, or null outside every biome.</summary>
    public static BiomeDefinition? BiomeAt(RegionDefinition region, float x, float z)
    {
        ArgumentNullException.ThrowIfNull(region);
        BiomeDefinition? best = null;
        float most = 0;
        foreach (BiomeDefinition biome in region.Biomes)
        {
            float weight = Weight(biome, x, z);
            if (weight > most)
            {
                (best, most) = (biome, weight);
            }
        }

        return best;
    }

    // The weighted hill scale and height offset at a point (the unshaped ground fills whatever weight the biomes leave).
    private static (float Scale, float Offset) Blend(RegionDefinition region, float x, float z)
    {
        float total = 0;
        float scale = 0;
        float offset = 0;
        foreach (BiomeDefinition biome in region.Biomes)
        {
            float weight = Weight(biome, x, z);
            total += weight;
            scale += weight * biome.HeightScale;
            offset += weight * biome.HeightOffset;
        }

        if (total > 1)
        {
            return (scale / total, offset / total);
        }

        return (scale + (1 - total), offset);
    }

    private static float Rim(float x, float z, float size)
    {
        float edge = MathF.Min(MathF.Min(x, z), MathF.Min(size - x, size - z));
        float t = Math.Clamp(1 - (edge / RimWidth), 0, 1);
        return RimHeight * t * t;
    }
}
