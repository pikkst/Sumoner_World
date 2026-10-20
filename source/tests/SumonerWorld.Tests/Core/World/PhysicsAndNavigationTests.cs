using System.Numerics;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Jobs;
using SumonerWorld.Core.Movement;
using SumonerWorld.Core.Navigation;
using SumonerWorld.Core.Physics;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.World;
using SumonerWorld.Scenarios;

namespace SumonerWorld.Tests.Core.World;

public class PhysicsAndNavigationTests
{
    private static readonly Heightfield Field = Heightfield.Generate(3, new HeightfieldSettings { Samples = 257 });

    [Fact]
    public void Rays_hit_the_heightfield_surface_and_flat_ground_without_one()
    {
        var physics = new HeightfieldPhysics(Field);
        var origin = new Vector3(100, 80, 100);

        Assert.True(physics.Raycast(origin, -Vector3.UnitY, 200, out RayHit down));
        Assert.Equal(Field.HeightAt(100, 100), down.Point.Y, 2);
        Assert.Equal(80 - Field.HeightAt(100, 100), down.Distance, 2);
        Assert.True(down.Normal.Y > 0.5f);

        Assert.True(physics.Raycast(origin, new Vector3(1, -0.5f, 0.3f), 500, out RayHit slanted));
        Assert.Equal(Field.HeightAt(slanted.Point.X, slanted.Point.Z), slanted.Point.Y, 2);

        Assert.False(physics.Raycast(origin, Vector3.UnitY, 100, out _));
        Assert.False(physics.Raycast(origin, -Vector3.UnitY, 10, out _));

        var flat = new HeightfieldPhysics(null);
        Assert.True(flat.Raycast(new Vector3(5, 3, 5), -Vector3.UnitY, 10, out RayHit floor));
        Assert.Equal(0, floor.Point.Y, 3);
        Assert.Equal(Vector3.UnitY, floor.Normal);
    }

    [Fact]
    public void Line_of_sight_is_blocked_by_a_hill_between_two_points_below_its_top()
    {
        var physics = new HeightfieldPhysics(Field);
        (Vector3 low, float lowHeight) = (default, float.MaxValue);
        (Vector3 high, float highHeight) = (default, float.MinValue);
        for (int x = 20; x < 236; x += 4)
        {
            float h = Field.HeightAt(x, 128);
            (low, lowHeight) = h < lowHeight ? (new Vector3(x, h, 128), h) : (low, lowHeight);
            (high, highHeight) = h > highHeight ? (new Vector3(x, h, 128), h) : (high, highHeight);
        }

        Vector3 aboveHill = high + new Vector3(0, 60, 0);
        Assert.True(physics.IsClear(aboveHill, aboveHill + new Vector3(30, 0, 0)));
        Assert.False(physics.IsClear(low + new Vector3(0, 0.5f, 0), high - new Vector3(0, 0.5f, 0)));
    }

    [Fact]
    public void Paths_requested_in_a_tick_arrive_at_the_next_job_stage_in_request_order_and_skip_destroyed_entities()
    {
        var world = new EcsWorld(8);
        Entity a = world.Create();
        Entity b = world.Create();
        Entity c = world.Create();
        var planner = new PathPlanner(new TwoLegNavigation(), world, capacity: 3);
        var sink = new RecordingSink();
        planner.Sink = sink;
        using var jobs = new JobScheduler(workerCount: 2, capacity: 4);

        Assert.True(planner.Request(a, Vector3.Zero, new Vector3(10, 0, 0), 0.1f));
        Assert.True(planner.Request(b, Vector3.Zero, new Vector3(0, 0, 10), 0.2f));
        Assert.True(planner.Request(c, Vector3.Zero, new Vector3(5, 0, 5), 0.3f));
        Assert.False(planner.Request(a, Vector3.Zero, Vector3.One, 0.1f));
        planner.Submit(jobs);
        Assert.False(planner.Request(a, Vector3.Zero, Vector3.One, 0.1f));
        world.Destroy(b);
        Assert.Empty(sink.Paths);

        jobs.ApplyCompleted();

        Assert.Equal([a, c], sink.Paths.Select(path => path.Entity));
        Assert.Equal([new Vector3(5, 0, 0), new Vector3(10, 0, 0)], sink.Paths[0].Waypoints);
        Assert.Equal(0.3f, sink.Paths[1].Speed);
        Assert.Equal(2, planner.Refused);
        Assert.Equal(0, planner.Waiting);
        Assert.True(planner.Request(a, Vector3.Zero, Vector3.One, 0.1f));
    }

    [Fact]
    public void Movement_follows_every_waypoint_on_the_ground_and_reports_only_at_the_end()
    {
        using CreatureWorld world = new(ShippedData.Current, new CreatureWorldSettings { Ground = Field, Navigation = new TwoLegNavigation() }, seed: 5);
        var scheduler = new SimulationScheduler(world.Clock, world.Events);
        CreatureSystems systems = world.CreateSystems();
        scheduler.Register(systems.JobApply);
        scheduler.Register(systems.Movement);
        ushort species = world.SpeciesIndex("mossback");
        Entity creature = world.SpawnCreature(species, new Vector3(50, 0, 50), Personality.Roll(world.Data.Species[species], world.Random), 0);
        Assert.Equal(Field.HeightAt(50, 50), world.World.GetComponent<Position>(creature).Value.Y);

        world.Paths.Request(creature, new Vector3(50, 0, 50), new Vector3(70, 0, 50), 0.5f);
        world.Paths.Submit(world.Jobs);
        var visited = new List<Vector3>();
        for (int tick = 0; tick < 60; tick++)
        {
            scheduler.RunTick();
            Vector3 position = world.World.GetComponent<Position>(creature).Value;
            Assert.Equal(Field.HeightAt(position.X, position.Z), position.Y, 4);
            visited.Add(position);
        }

        Assert.Contains(visited, p => Vector3.Distance(p with { Y = 0 }, new Vector3(60, 0, 50)) < 0.01f);
        Vector3 end = world.World.GetComponent<Position>(creature).Value;
        Assert.Equal(new Vector3(70, 0, 50), end with { Y = 0 });
        Assert.Equal(0, world.World.GetComponent<MoveTarget>(creature).Active);
    }

    [Fact]
    public void A_creature_without_a_path_stays_and_its_tree_hears_the_failure()
    {
        using CreatureWorld world = new(ShippedData.Current, new CreatureWorldSettings { Navigation = new NoPath() }, seed: 5);
        var scheduler = new SimulationScheduler(world.Clock, world.Events);
        CreatureSystems systems = world.CreateSystems();
        scheduler.Register(systems.JobApply);
        scheduler.Register(systems.Movement);
        ushort species = world.SpeciesIndex("mossback");
        Entity creature = world.SpawnCreature(species, new Vector3(50, 0, 50), Personality.Roll(world.Data.Species[species], world.Random), 0);

        world.Paths.Request(creature, new Vector3(50, 0, 50), new Vector3(70, 0, 50), 0.5f);
        world.Paths.Submit(world.Jobs);
        scheduler.RunTick();
        scheduler.RunTick();

        Assert.Equal(new Vector3(50, 0, 50), world.World.GetComponent<Position>(creature).Value);
        Assert.Equal(0, world.World.GetComponent<MoveTarget>(creature).Active);
        Assert.Equal(1, world.Paths.Delivered);
    }

    [Fact]
    public void Player_movement_stands_on_the_ground_the_same_way_on_server_and_client()
    {
        var server = new MovementModel(new MovementSettings { Ground = Field, BoundsMax = new Vector3(256, 0, 256) });
        var client = new MovementModel(new MovementSettings { Ground = DevRegionLike(), BoundsMax = new Vector3(256, 0, 256) });
        Vector3 a = new(100, 0, 100);
        Vector3 b = a;
        for (int i = 0; i < 200; i++)
        {
            a = server.Advance(a, new MoveInput(700, 300));
            b = client.Advance(b, new MoveInput(700, 300));
        }

        Assert.Equal(a, b);
        Assert.Equal(Field.HeightAt(a.X, a.Z), a.Y);
        Assert.Equal(0, new MovementModel(new MovementSettings()).Advance(new Vector3(1, 7, 1), MoveInput.None).Y);
    }

    private static Heightfield DevRegionLike() => Heightfield.Generate(3, new HeightfieldSettings { Samples = 257 });

    private sealed class TwoLegNavigation : INavigation
    {
        public PathStatus FindPath(Vector3 start, Vector3 goal, Span<Vector3> waypoints, out int count)
        {
            waypoints[0] = (start + goal) / 2;
            waypoints[1] = goal;
            count = 2;
            return PathStatus.Found;
        }
    }

    private sealed class NoPath : INavigation
    {
        public PathStatus FindPath(Vector3 start, Vector3 goal, Span<Vector3> waypoints, out int count)
        {
            count = 0;
            return PathStatus.NotFound;
        }
    }

    private sealed class RecordingSink : IPathSink
    {
        public List<(Entity Entity, Vector3[] Waypoints, float Speed)> Paths { get; } = [];

        public void OnPath(Entity entity, PathStatus status, ReadOnlySpan<Vector3> waypoints, float speed) =>
            Paths.Add((entity, waypoints.ToArray(), speed));
    }
}
