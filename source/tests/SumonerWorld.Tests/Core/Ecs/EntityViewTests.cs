using SumonerWorld.Core.Ecs;

namespace SumonerWorld.Tests.Core.Ecs;

public class EntityViewTests
{
    private const int Capacity = 256;

    [Fact]
    public void A_view_returns_exactly_the_entities_that_have_every_component()
    {
        var world = CreateWorld();
        Entity both = world.Create();
        world.Add(both, new Position(1));
        world.Add(both, new Velocity(1));
        Entity onlyPosition = world.Create();
        world.Add(onlyPosition, new Position(2));
        Entity onlyVelocity = world.Create();
        world.Add(onlyVelocity, new Velocity(3));

        Assert.Equal([both], Collect(world.View<Position, Velocity>()));
    }

    [Fact]
    public void Three_and_four_component_views_intersect_correctly_whichever_store_is_smallest()
    {
        var world = CreateWorld();
        var expected = new List<Entity>();
        for (int i = 0; i < 40; i++)
        {
            Entity entity = world.Create();
            world.Add(entity, new Position(i));
            if (i % 2 == 0)
            {
                world.Add(entity, new Velocity(i));
            }

            if (i % 3 == 0)
            {
                world.Add(entity, new Tier(i));
            }

            if (i % 5 == 0)
            {
                world.Add(entity, new Tag());
            }

            if (i % 2 == 0 && i % 3 == 0 && i % 5 == 0)
            {
                expected.Add(entity);
            }
        }

        // Every argument order must give the same set; the rarest store (Tag) drives iteration.
        Assert.Equal(expected, Sorted(Collect(world.View<Position, Velocity, Tier, Tag>())));
        Assert.Equal(expected, Sorted(Collect(world.View<Tag, Tier, Velocity, Position>())));
        Assert.Equal(
            Sorted(Collect(world.View<Velocity, Tier, Position>())),
            Sorted(Collect(world.View<Tier, Position, Velocity>())));
    }

    [Fact]
    public void Two_worlds_given_the_same_operations_iterate_in_the_same_order()
    {
        Assert.Equal(Collect(BuildChurnedWorld().View<Position, Velocity>()), Collect(BuildChurnedWorld().View<Position, Velocity>()));
    }

    [Fact]
    public void Structural_changes_during_iteration_are_rejected()
    {
        var world = CreateWorld();
        for (int i = 0; i < 3; i++)
        {
            Entity entity = world.Create();
            world.Add(entity, new Position(i));
            world.Add(entity, new Velocity(i));
        }

        Assert.Throws<InvalidOperationException>(() =>
        {
            foreach (Entity entity in world.View<Position, Velocity>())
            {
                world.Remove<Velocity>(entity);
                world.Remove<Position>(entity);
            }
        });
    }

    [Fact]
    public void Steady_state_iteration_and_component_updates_allocate_nothing()
    {
        const int creatures = 200;
        const int warmUpTicks = 5;
        const int measuredTicks = 500;
        var world = CreateWorld();
        for (int i = 0; i < creatures; i++)
        {
            Entity entity = world.Create();
            world.Add(entity, new Position(i));
            world.Add(entity, new Velocity(1));
        }

        for (int i = 0; i < warmUpTicks; i++)
        {
            Move(world);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < measuredTicks; i++)
        {
            Move(world);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static void Move(EcsWorld world)
    {
        ComponentStore<Position> positions = world.Store<Position>();
        ComponentStore<Velocity> velocities = world.Store<Velocity>();
        foreach (Entity entity in world.View<Position, Velocity>())
        {
            ref Position position = ref positions.Get(entity);
            position = new Position(position.X + velocities.Get(entity).X);
        }
    }

    private static EcsWorld BuildChurnedWorld()
    {
        var world = CreateWorld();
        var entities = new List<Entity>();
        for (int i = 0; i < 50; i++)
        {
            Entity entity = world.Create();
            world.Add(entity, new Position(i));
            world.Add(entity, new Velocity(i));
            entities.Add(entity);
        }

        for (int i = 0; i < 50; i += 7)
        {
            world.Destroy(entities[i]);
        }

        for (int i = 0; i < 5; i++)
        {
            Entity entity = world.Create();
            world.Add(entity, new Velocity(100 + i));
            world.Add(entity, new Position(100 + i));
        }

        return world;
    }

    private static List<Entity> Collect(EntityView view)
    {
        var result = new List<Entity>();
        foreach (Entity entity in view)
        {
            result.Add(entity);
        }

        return result;
    }

    private static List<Entity> Sorted(List<Entity> entities) => [.. entities.OrderBy(entity => entity.Index)];

    private static EcsWorld CreateWorld()
    {
        var world = new EcsWorld(Capacity);
        world.Register<Position>();
        world.Register<Velocity>();
        world.Register<Tier>();
        world.Register<Tag>();
        return world;
    }

    private readonly record struct Position(int X);

    private readonly record struct Velocity(int X);

    private readonly record struct Tier(int Value);

    private readonly record struct Tag;
}
