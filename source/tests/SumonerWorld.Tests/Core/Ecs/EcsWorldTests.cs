using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Randomness;

namespace SumonerWorld.Tests.Core.Ecs;

public class EcsWorldTests
{
    private const int Capacity = 64;

    [Fact]
    public void Created_entities_are_alive_and_counted()
    {
        var world = CreateWorld();

        Entity a = world.Create();
        Entity b = world.Create();

        Assert.True(world.IsAlive(a));
        Assert.True(world.IsAlive(b));
        Assert.NotEqual(a, b);
        Assert.Equal(2, world.EntityCount);
    }

    [Fact]
    public void A_destroyed_entity_handle_never_reaches_the_entity_that_reuses_its_slot()
    {
        var world = CreateWorld();
        Entity old = world.Create();
        world.Add(old, new Health(10));
        world.Destroy(old);

        Entity reused = world.Create();
        world.Add(reused, new Health(99));

        Assert.Equal(old.Index, reused.Index);
        Assert.False(world.IsAlive(old));
        Assert.False(world.Has<Health>(old));
        Assert.Throws<InvalidOperationException>(() => world.GetComponent<Health>(old));
        Assert.Equal(99, world.GetComponent<Health>(reused).Value);
    }

    [Fact]
    public void Freed_slots_are_reused_last_in_first_out()
    {
        var world = CreateWorld();
        Entity a = world.Create();
        Entity b = world.Create();
        world.Destroy(a);
        world.Destroy(b);

        Assert.Equal(b.Index, world.Create().Index);
        Assert.Equal(a.Index, world.Create().Index);
    }

    [Fact]
    public void Exceeding_capacity_fails_with_an_actionable_message()
    {
        var world = new EcsWorld(capacity: 2);
        world.Create();
        world.Create();

        var error = Assert.Throws<InvalidOperationException>(() => world.Create());
        Assert.Contains("capacity", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Components_can_be_added_read_changed_in_place_and_removed()
    {
        var world = CreateWorld();
        Entity entity = world.Create();

        world.Add(entity, new Health(10));
        world.GetComponent<Health>(entity) = new Health(7);

        Assert.True(world.Has<Health>(entity));
        Assert.Equal(7, world.GetComponent<Health>(entity).Value);
        Assert.True(world.Remove<Health>(entity));
        Assert.False(world.Has<Health>(entity));
        Assert.False(world.Remove<Health>(entity));
    }

    [Fact]
    public void Adding_the_same_component_twice_is_rejected()
    {
        var world = CreateWorld();
        Entity entity = world.Create();
        world.Add(entity, new Health(1));

        Assert.Throws<InvalidOperationException>(() => world.Add(entity, new Health(2)));
    }

    [Fact]
    public void Destroy_removes_every_component()
    {
        var world = CreateWorld();
        Entity entity = world.Create();
        world.Add(entity, new Health(1));
        world.Add(entity, new Speed(2));

        world.Destroy(entity);

        Assert.Equal(0, world.Store<Health>().Count);
        Assert.Equal(0, world.Store<Speed>().Count);
    }

    [Fact]
    public void Operations_on_dead_entities_and_unregistered_types_fail_loudly()
    {
        var world = CreateWorld();
        Entity entity = world.Create();
        world.Destroy(entity);

        Assert.Throws<InvalidOperationException>(() => world.Destroy(entity));
        Assert.Throws<InvalidOperationException>(() => world.Add(entity, new Health(1)));
        Assert.Throws<InvalidOperationException>(() => world.Store<Unregistered>());
        Assert.Throws<InvalidOperationException>(() => world.Register<Health>());
    }

    [Fact]
    public void Random_operations_match_a_simple_reference_model()
    {
        // Property test: 20,000 seeded random operations; after each one the ECS must agree with a dictionary model.
        const int operations = 20_000;
        const int operationKinds = 4;
        var random = new SeededRandom(0x5EED);
        var world = CreateWorld();
        var model = new Dictionary<Entity, int?>();
        var live = new List<Entity>();

        for (int step = 0; step < operations; step++)
        {
            int kind = random.NextInt(0, operationKinds);
            if (kind == 0 && world.EntityCount < Capacity)
            {
                Entity created = world.Create();
                live.Add(created);
                model[created] = null;
            }
            else if (live.Count > 0)
            {
                Entity target = live[random.NextInt(0, live.Count)];
                ApplyRandomChange(world, model, live, target, kind, step);
            }

            AssertMatchesModel(world, model);
        }
    }

    private static void ApplyRandomChange(EcsWorld world, Dictionary<Entity, int?> model, List<Entity> live, Entity target, int kind, int step)
    {
        switch (kind)
        {
            case 1 when model[target] is null:
                world.Add(target, new Health(step));
                model[target] = step;
                break;
            case 1:
            case 2:
                Assert.Equal(model[target] is not null, world.Remove<Health>(target));
                model[target] = null;
                break;
            default:
                world.Destroy(target);
                live.Remove(target);
                model.Remove(target);
                break;
        }
    }

    private static void AssertMatchesModel(EcsWorld world, Dictionary<Entity, int?> model)
    {
        ComponentStore<Health> store = world.Store<Health>();
        Assert.Equal(model.Count, world.EntityCount);
        Assert.Equal(model.Values.Count(value => value is not null), store.Count);

        ReadOnlySpan<Entity> entities = store.Entities;
        Span<Health> components = store.Components;
        for (int i = 0; i < entities.Length; i++)
        {
            Assert.Equal(model[entities[i]], components[i].Value);
        }
    }

    private static EcsWorld CreateWorld()
    {
        var world = new EcsWorld(Capacity);
        world.Register<Health>();
        world.Register<Speed>();
        return world;
    }

    private readonly record struct Health(int Value);

    private readonly record struct Speed(int Value);

    private readonly record struct Unregistered;
}
