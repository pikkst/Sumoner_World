using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Core.Ecs;

namespace SumonerWorld.Core.Combat;

/// <summary>The transient combat components (moves, yield, defeat, defence): registration and the state hash (TDD §2.3).</summary>
public static class CombatState
{
    /// <summary>Registers the components with a world (<see cref="Combatant"/> is registered by its owner).</summary>
    public static void Register(EcsWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        world.Register<ActiveMove>();
        world.Register<Yielding>();
        world.Register<Downed>();
        world.Register<Blocking>();
        world.Register<Dodging>();
        world.Register<Staggered>();
        world.Register<StatusEffects>();
        world.Register<Items.Fighters>();
    }

    /// <summary>Adds the hashed components, with the entities that hold them.</summary>
    public static void AddTo(ref StateHash hash, EcsWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        hash.Add<Entity>(world.Store<ActiveMove>().Entities);
        hash.Add<ActiveMove>(world.Store<ActiveMove>().Components);
        hash.Add<Entity>(world.Store<Yielding>().Entities);
        hash.Add<Yielding>(world.Store<Yielding>().Components);
        hash.Add<Entity>(world.Store<Blocking>().Entities);
        hash.Add<Blocking>(world.Store<Blocking>().Components);
        hash.Add<Entity>(world.Store<Dodging>().Entities);
        hash.Add<Dodging>(world.Store<Dodging>().Components);
        hash.Add<Entity>(world.Store<Staggered>().Entities);
        hash.Add<Staggered>(world.Store<Staggered>().Components);
        hash.Add<Entity>(world.Store<StatusEffects>().Entities);
        hash.Add<StatusEffects>(world.Store<StatusEffects>().Components);
        hash.Add<Entity>(world.Store<Items.Fighters>().Entities);
        hash.Add<Items.Fighters>(world.Store<Items.Fighters>().Components);
    }
}
