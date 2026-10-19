using System.Numerics;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Randomness;
using SumonerWorld.Core.Spatial;

namespace SumonerWorld.Tests.Core.Spatial;

public class SpatialGridTests
{
    private const int Capacity = 1_024;
    private const float RegionSize = 512f;
    private const float CellSize = 16f;
    private const int BufferSize = 1_024;

    [Fact]
    public void Radius_query_finds_entities_within_range_on_the_ground_plane()
    {
        var grid = CreateGrid();
        var near = new Entity(0, 0);
        var far = new Entity(1, 0);
        var high = new Entity(2, 0);
        grid.Insert(near, new Vector3(5, 0, 0));
        grid.Insert(far, new Vector3(50, 0, 0));
        grid.Insert(high, new Vector3(0, 300, 3));

        Assert.Equal([near, high], Sorted(QueryRadius(grid, Vector3.Zero, 10f)));
    }

    [Fact]
    public void Moving_across_cells_updates_query_results()
    {
        var grid = CreateGrid();
        var entity = new Entity(7, 0);
        grid.Insert(entity, new Vector3(0, 0, 0));

        grid.Move(entity, new Vector3(100, 0, 100));

        Assert.Empty(QueryRadius(grid, Vector3.Zero, 20f));
        Assert.Equal([entity], QueryRadius(grid, new Vector3(100, 0, 100), 1f));
    }

    [Fact]
    public void Removed_and_stale_entities_are_not_found_or_accepted()
    {
        var grid = CreateGrid();
        var entity = new Entity(3, 0);
        grid.Insert(entity, Vector3.Zero);

        Assert.True(grid.Remove(entity));
        Assert.False(grid.Remove(entity));
        Assert.Empty(QueryRadius(grid, Vector3.Zero, 5f));

        var reused = new Entity(3, 1);
        grid.Insert(reused, Vector3.Zero);
        Assert.False(grid.Contains(entity));
        Assert.Throws<InvalidOperationException>(() => grid.Move(entity, Vector3.One));
        Assert.Throws<InvalidOperationException>(() => grid.Insert(entity, Vector3.One));
    }

    [Fact]
    public void Positions_outside_the_bounds_are_still_found()
    {
        var grid = CreateGrid();
        var outside = new Entity(1, 0);
        grid.Insert(outside, new Vector3(RegionSize, 0, RegionSize));

        Assert.Equal([outside], QueryRadius(grid, new Vector3(RegionSize - 1, 0, RegionSize - 1), 2f));
    }

    [Fact]
    public void Cone_query_only_returns_entities_in_front_within_the_half_angle()
    {
        var grid = CreateGrid();
        var ahead = new Entity(0, 0);
        var edge = new Entity(1, 0);
        var outsideAngle = new Entity(2, 0);
        var behind = new Entity(3, 0);
        grid.Insert(ahead, new Vector3(0, 0, 10));
        grid.Insert(edge, new Vector3(5, 0, 9));
        grid.Insert(outsideAngle, new Vector3(10, 0, 1));
        grid.Insert(behind, new Vector3(0, 0, -10));

        var buffer = new Entity[BufferSize];
        SpatialQueryResult result = grid.QueryCone(Vector3.Zero, Vector3.UnitZ, 20f, MathF.PI / 4, buffer);

        Assert.Equal([ahead, edge], Sorted(buffer.AsSpan(0, result.Count).ToArray()));
    }

    [Fact]
    public void A_full_buffer_is_reported_as_truncated()
    {
        var grid = CreateGrid();
        for (int i = 0; i < 5; i++)
        {
            grid.Insert(new Entity(i, 0), new Vector3(i, 0, 0));
        }

        var buffer = new Entity[3];
        SpatialQueryResult result = grid.QueryRadius(Vector3.Zero, 10f, buffer);

        Assert.Equal(new SpatialQueryResult(3, Truncated: true), result);
    }

    [Fact]
    public void Queries_match_brute_force_on_random_scenes()
    {
        // Property test: 500 seeded random entities, 300 random radius and cone queries, each compared with a brute-force scan.
        const int entities = 500;
        const int queries = 300;
        var random = new SeededRandom(0xC0DE);
        var grid = CreateGrid();
        var positions = new Dictionary<Entity, Vector3>();
        for (int i = 0; i < entities; i++)
        {
            var entity = new Entity(i, 0);
            Vector3 position = RandomPosition(random);
            grid.Insert(entity, position);
            positions[entity] = position;
        }

        for (int q = 0; q < queries; q++)
        {
            Vector3 center = RandomPosition(random);
            float range = (float)(random.NextDouble() * 80);
            Vector3 forward = new((float)(random.NextDouble() - 0.5), 0, (float)(random.NextDouble() - 0.5));
            float halfAngle = (float)(random.NextDouble() * MathF.PI);

            Assert.Equal(BruteForce(positions, center, range, null, 0), Sorted(QueryRadius(grid, center, range)));

            var buffer = new Entity[BufferSize];
            SpatialQueryResult cone = grid.QueryCone(center, forward, range, halfAngle, buffer);
            Assert.Equal(BruteForce(positions, center, range, forward, halfAngle), Sorted(buffer.AsSpan(0, cone.Count).ToArray()));
        }
    }

    [Fact]
    public void Two_grids_given_the_same_operations_return_the_same_order()
    {
        Assert.Equal(QueryRadius(BuildScene(), Vector3.Zero, 200f), QueryRadius(BuildScene(), Vector3.Zero, 200f));
    }

    [Fact]
    public void Steady_state_moves_and_queries_allocate_nothing()
    {
        const int creatures = 200;
        const int ticks = 100;
        var grid = CreateGrid();
        var random = new SeededRandom(7);
        for (int i = 0; i < creatures; i++)
        {
            grid.Insert(new Entity(i, 0), RandomPosition(random));
        }

        var buffer = new Entity[64];
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int tick = 0; tick < ticks; tick++)
        {
            for (int i = 0; i < creatures; i++)
            {
                var entity = new Entity(i, 0);
                grid.Move(entity, new Vector3(i % 400, 0, (tick * 3 + i) % 400));
                grid.QueryRadius(new Vector3(i % 400, 0, tick % 400), 40f, buffer);
            }
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void Invalid_input_is_rejected()
    {
        var grid = CreateGrid();
        var buffer = new Entity[4];

        Assert.Throws<ArgumentOutOfRangeException>(() => grid.Insert(new Entity(Capacity, 0), Vector3.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.Insert(new Entity(0, 0), new Vector3(float.NaN, 0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.QueryRadius(Vector3.Zero, -1f, buffer));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.QueryCone(Vector3.Zero, Vector3.UnitY, 5f, 1f, buffer));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpatialGridSettings(0, 0, RegionSize, RegionSize, 0f, Capacity));
    }

    private static SpatialGrid BuildScene()
    {
        var grid = CreateGrid();
        var random = new SeededRandom(99);
        for (int i = 0; i < 100; i++)
        {
            grid.Insert(new Entity(i, 0), RandomPosition(random));
        }

        for (int i = 0; i < 100; i += 3)
        {
            grid.Move(new Entity(i, 0), RandomPosition(random));
        }

        return grid;
    }

    private static List<Entity> BruteForce(Dictionary<Entity, Vector3> positions, Vector3 center, float range, Vector3? forward, float halfAngle)
    {
        var result = new List<Entity>();
        foreach ((Entity entity, Vector3 position) in positions)
        {
            var offset = new Vector2(position.X - center.X, position.Z - center.Z);
            if (offset.LengthSquared() > range * range)
            {
                continue;
            }

            if (forward is Vector3 f && offset.LengthSquared() > 0)
            {
                var direction = Vector2.Normalize(new Vector2(f.X, f.Z));
                float cos = Vector2.Dot(Vector2.Normalize(offset), direction);
                if (MathF.Acos(Math.Clamp(cos, -1f, 1f)) > halfAngle + 1e-4f)
                {
                    continue;
                }
            }

            result.Add(entity);
        }

        return Sorted(result);
    }

    private static Vector3 RandomPosition(SeededRandom random) =>
        new((float)(random.NextDouble() * RegionSize), 0, (float)(random.NextDouble() * RegionSize));

    private static List<Entity> QueryRadius(SpatialGrid grid, Vector3 center, float radius)
    {
        var buffer = new Entity[BufferSize];
        SpatialQueryResult result = grid.QueryRadius(center, radius, buffer);
        Assert.False(result.Truncated);
        return [.. buffer.AsSpan(0, result.Count).ToArray()];
    }

    private static List<Entity> Sorted(IEnumerable<Entity> entities) => [.. entities.OrderBy(entity => entity.Index)];

    private static SpatialGrid CreateGrid() =>
        new(new SpatialGridSettings(0, 0, RegionSize, RegionSize, CellSize, Capacity));
}
