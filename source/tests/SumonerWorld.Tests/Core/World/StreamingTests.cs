using System.Numerics;
using SumonerWorld.Core.World;

namespace SumonerWorld.Tests.Core.World;

public class StreamingTests
{
    [Fact]
    public void Rings_follow_the_distance_to_the_nearest_point_of_each_chunk()
    {
        var planner = new StreamingPlanner(new StreamingSettings { RegionChunks = 16, MaxUpgradesPerUpdate = 1000 });
        var changes = new List<ChunkChange>();
        var viewer = new Vector3(32, 0, 32);

        planner.Update(viewer, changes);

        Assert.Equal(StreamRing.Full, planner.RingOf(new ChunkCoord(0, 0)));
        Assert.Equal(StreamRing.Full, planner.RingOf(new ChunkCoord(2, 1)));     // nearest point (128, 64): 101 m
        Assert.Equal(StreamRing.Medium, planner.RingOf(new ChunkCoord(2, 2)));   // nearest point (128, 128): 136 m
        Assert.Equal(StreamRing.Medium, planner.RingOf(new ChunkCoord(3, 0)));   // 160 m
        Assert.Equal(StreamRing.Medium, planner.RingOf(new ChunkCoord(6, 0)));   // 352 m
        Assert.Equal(StreamRing.Far, planner.RingOf(new ChunkCoord(7, 0)));      // 416 m
        Assert.Equal(StreamRing.None, planner.RingOf(new ChunkCoord(99, 0)));    // outside the region
        Assert.Equal(256, changes.Count);
        Assert.All(changes, change => Assert.Equal(StreamRing.None, change.From));
        Assert.Equal(0, planner.Pending(viewer));
    }

    [Fact]
    public void Upgrades_are_limited_per_update_and_the_nearest_chunks_come_first()
    {
        var planner = new StreamingPlanner(new StreamingSettings { RegionChunks = 8, MaxUpgradesPerUpdate = 4 });
        var changes = new List<ChunkChange>();
        var viewer = new Vector3(256, 0, 256);

        planner.Update(viewer, changes);

        Assert.Equal(4, changes.Count);
        Assert.All(changes, change => Assert.Contains(change.Chunk, new[] { new ChunkCoord(3, 3), new ChunkCoord(4, 3), new ChunkCoord(3, 4), new ChunkCoord(4, 4) }));
        Assert.Equal(60, planner.Pending(viewer));

        int updates = 1;
        while (planner.Pending(viewer) > 0)
        {
            planner.Update(viewer, changes);
            Assert.InRange(changes.Count, 1, 4);
            updates++;
        }

        Assert.Equal(16, updates);
        Assert.Equal(64, planner.Count(StreamRing.Full) + planner.Count(StreamRing.Medium));
    }

    [Fact]
    public void Moving_away_downgrades_at_once_but_only_past_the_hysteresis()
    {
        var planner = new StreamingPlanner(new StreamingSettings { RegionChunks = 16, MaxUpgradesPerUpdate = 1000 });
        var changes = new List<ChunkChange>();
        var chunk = new ChunkCoord(0, 0);
        planner.Update(new Vector3(32, 0, 32), changes);
        Assert.Equal(StreamRing.Full, planner.RingOf(chunk));

        // 136 m from the chunk: beyond the 128 m full radius but inside the 16 m hysteresis.
        planner.Update(new Vector3(64 + 136, 0, 32), changes);
        Assert.Equal(StreamRing.Full, planner.RingOf(chunk));

        planner.Update(new Vector3(64 + 150, 0, 32), changes);
        Assert.Equal(StreamRing.Medium, planner.RingOf(chunk));
        Assert.Contains(new ChunkChange(chunk, StreamRing.Full, StreamRing.Medium), changes);

        // Coming back upgrades again.
        planner.Update(new Vector3(32, 0, 32), changes);
        Assert.Equal(StreamRing.Full, planner.RingOf(chunk));
    }

    [Fact]
    public void Ring_radii_must_grow()
    {
        Assert.Throws<ArgumentException>(() => new StreamingPlanner(new StreamingSettings { FullRadius = 500, MediumRadius = 400 }));
    }

    [Fact]
    public void Scatter_is_the_same_for_the_same_seed_and_chunk_and_stays_inside_the_chunk()
    {
        var first = new ScatteredProp[ChunkScatter.Count];
        var again = new ScatteredProp[ChunkScatter.Count];
        var neighbour = new ScatteredProp[ChunkScatter.Count];
        ChunkScatter.Generate(42, new ChunkCoord(3, -2), first);
        ChunkScatter.Generate(42, new ChunkCoord(3, -2), again);
        ChunkScatter.Generate(42, new ChunkCoord(-2, 3), neighbour);

        Assert.Equal(first, again);
        Assert.NotEqual(first, neighbour);
        Assert.All(first, prop => Assert.InRange(prop.X, 0, ChunkCoord.Size));
        Assert.All(first[..ChunkScatter.MediumCount], prop => Assert.NotEqual(PropKind.Bush, prop.Kind));
        Assert.All(first[ChunkScatter.MediumCount..], prop => Assert.Equal(PropKind.Bush, prop.Kind));
        Assert.Throws<ArgumentException>(() => ChunkScatter.Generate(42, default, new ScatteredProp[10]));
    }

    [Fact]
    public void Heightfield_is_reproducible_smooth_and_within_its_amplitude()
    {
        var settings = new HeightfieldSettings { Samples = 129, Amplitude = 40 };
        Heightfield field = Heightfield.Generate(7, settings);
        Heightfield again = Heightfield.Generate(7, settings);
        Heightfield other = Heightfield.Generate(8, settings);

        Assert.Equal(128f, field.Size);
        Assert.True(field.Heights.SequenceEqual(again.Heights));
        Assert.False(field.Heights.SequenceEqual(other.Heights));
        float min = float.MaxValue;
        float max = float.MinValue;
        foreach (float height in field.Heights)
        {
            (min, max) = (MathF.Min(min, height), MathF.Max(max, height));
        }

        Assert.InRange(min, 0, 40);
        Assert.InRange(max, min + 2, 40);

        // Bilinear between samples, exact on them, clamped outside.
        Assert.Equal(field.Heights[(3 * 129) + 5], field.HeightAt(5, 3));
        Assert.Equal((field.HeightAt(5, 3) + field.HeightAt(6, 3)) / 2, field.HeightAt(5.5f, 3), 4);
        Assert.Equal(field.HeightAt(0, 0), field.HeightAt(-10, -10));
        Assert.Equal(field.HeightAt(128, 128), field.HeightAt(500, 900));

        // Neighbouring 1 m samples never jump more than a few metres (no cliffs from the noise).
        for (int i = 1; i < field.Heights.Length; i++)
        {
            if (i % 129 != 0)
            {
                Assert.InRange(MathF.Abs(field.Heights[i] - field.Heights[i - 1]), 0, 3);
            }
        }
    }

    [Fact]
    public void Chunk_of_a_position_and_its_centre()
    {
        Assert.Equal(new ChunkCoord(0, 0), ChunkCoord.Containing(new Vector3(63.9f, 5, 0)));
        Assert.Equal(new ChunkCoord(-1, 1), ChunkCoord.Containing(new Vector3(-0.1f, 0, 64)));
        Assert.Equal(new Vector3(96, 0, 32), new ChunkCoord(1, 0).Centre);
    }
}
