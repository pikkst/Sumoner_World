using System.Numerics;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Cognition.Behaviour;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.World;
using SumonerWorld.Scenarios;

namespace SumonerWorld.Tests.Core.World;

public class PersonalSpaceTests
{
    [Fact]
    public void Creatures_on_the_same_spot_shuffle_apart_without_jumping()
    {
        using var world = new CreatureWorld(ShippedData.Current, new CreatureWorldSettings(), seed: 3);
        (SimulationScheduler scheduler, CreatureSystems systems) = Schedule(world);
        Entity a = Spawn(world, "mossback", new Vector3(100, 0, 100));
        Entity b = Spawn(world, "mossback", new Vector3(100, 0, 100));
        Entity fox = Spawn(world, "bogfox", new Vector3(100.5f, 0, 100.2f));

        Vector3 last = Position(world, a);
        for (int tick = 0; tick < 150; tick++)
        {
            scheduler.RunTick();
            Assert.True(Vector3.Distance(last, Position(world, a)) <= SeparationSystem.MaxStepPerTick + 1e-5f, "One small step per tick.");
            last = Position(world, a);
        }

        float mossback = Radius(world, "mossback");
        float bogfox = Radius(world, "bogfox");
        Assert.True(Ground(Position(world, a), Position(world, b)) >= (2 * mossback) - 0.01f, $"mossbacks {Ground(Position(world, a), Position(world, b))} m apart");
        Assert.True(Ground(Position(world, a), Position(world, fox)) >= mossback + bogfox - 0.01f);
        Assert.True(Ground(Position(world, b), Position(world, fox)) >= mossback + bogfox - 0.01f);
        Assert.True(systems.Separation.Corrections > 0);
    }

    [Fact]
    public void A_creature_steps_out_of_a_player_and_the_player_is_never_moved()
    {
        using var world = new CreatureWorld(ShippedData.Current, new CreatureWorldSettings(), seed: 4);
        (SimulationScheduler scheduler, _) = Schedule(world);
        Entity player = world.SpawnPlayerAvatar(1001, new Vector3(200, 0, 200));
        Entity creature = Spawn(world, "mossback", new Vector3(200.3f, 0, 200));

        for (int tick = 0; tick < 60; tick++)
        {
            scheduler.RunTick();
        }

        Assert.Equal(new Vector3(200, 0, 200), Position(world, player));
        Assert.True(Ground(Position(world, player), Position(world, creature)) >= Radius(world, "mossback") + BodySizes.PlayerRadius - 0.01f);
    }

    [Fact]
    public void Approaching_the_focus_stops_short_of_touching_it()
    {
        using var world = new CreatureWorld(ShippedData.Current, new CreatureWorldSettings { WalkSpeed = 0.5f }, seed: 5);
        (SimulationScheduler scheduler, _) = Schedule(world);
        Entity player = world.SpawnPlayerAvatar(1001, new Vector3(100, 0, 100));
        Entity creature = Spawn(world, "mossback", new Vector3(120, 0, 100));
        world.World.GetComponent<FocusRef>(creature) = new FocusRef(player, 100, 1);

        world.Events.Publish(new CreatureIntent(creature, IntentKind.MoveTo, MoveTargetMode.Focus, 100, default, default, null));
        for (int tick = 0; tick < 120; tick++)
        {
            scheduler.RunTick();
        }

        float expected = Radius(world, "mossback") + BodySizes.PlayerRadius + CreatureIntentSystem.ApproachGapMetres;
        Assert.Equal(expected, Ground(Position(world, player), Position(world, creature)), 2);
        Assert.Equal(0, world.World.GetComponent<MoveTarget>(creature).Active);
    }

    private static (SimulationScheduler Scheduler, CreatureSystems Systems) Schedule(CreatureWorld world)
    {
        var scheduler = new SimulationScheduler(world.Clock, world.Events);
        CreatureSystems systems = world.CreateSystems();
        scheduler.Register(systems.JobApply);
        scheduler.Register(systems.Intents);
        scheduler.Register(systems.Movement);
        scheduler.Register(systems.Separation);
        return (scheduler, systems);
    }

    private static Entity Spawn(CreatureWorld world, string species, Vector3 at) =>
        world.SpawnCreature(world.SpeciesIndex(species), at, new Personality(0, 0, 0, 0), group: 0);

    private static float Radius(CreatureWorld world, string species) => world.Data.GetSpecies(species).BodyRadiusCm / 100f;

    private static Vector3 Position(CreatureWorld world, Entity entity) => world.World.GetComponent<Position>(entity).Value;

    private static float Ground(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));
}
