using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Core.Ecs;

namespace SumonerWorld.Core.Bonding;

/// <summary>The transient bonding components (Pact answers, summoning sigils, companion states and orders): registration and the state hash (TDD §2.3).</summary>
public static class BondingComponents
{
    /// <summary>Registers the components with a world.</summary>
    public static void Register(EcsWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        world.Register<PactAnswer>();
        world.Register<SigilCast>();
        world.Register<CompanionState>();
        world.Register<CompanionOrder>();
        world.Register<RequestedMove>();
    }

    /// <summary>Adds the components, with the entities that hold them.</summary>
    public static void AddTo(ref StateHash hash, EcsWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        hash.Add<Entity>(world.Store<PactAnswer>().Entities);
        hash.Add<PactAnswer>(world.Store<PactAnswer>().Components);
        hash.Add<Entity>(world.Store<SigilCast>().Entities);
        hash.Add<SigilCast>(world.Store<SigilCast>().Components);
        hash.Add<Entity>(world.Store<CompanionState>().Entities);
        hash.Add<CompanionState>(world.Store<CompanionState>().Components);
        hash.Add<Entity>(world.Store<CompanionOrder>().Entities);
        hash.Add<CompanionOrder>(world.Store<CompanionOrder>().Components);
        hash.Add<Entity>(world.Store<RequestedMove>().Entities);
        hash.Add<RequestedMove>(world.Store<RequestedMove>().Components);
    }
}
