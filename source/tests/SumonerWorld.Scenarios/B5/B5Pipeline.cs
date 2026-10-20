using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.World;

namespace SumonerWorld.Scenarios.B5;

/// <summary>Systems whose counters the scenario reports.</summary>
internal sealed record B5Probes(CreatureSystems Creatures, PactOfferSystem Pacts, ChurnSystem Churn);

/// <summary>
/// Registers the production creature systems plus the B5 stand-ins (player route, crowd reaction, Pact offers, churn) in tick order.
/// </summary>
internal static class B5Pipeline
{
    public static B5Probes Register(CreatureWorld world, SimulationScheduler scheduler, B5Scenario scenario, ushort species, float regionSize, float playerSpeed)
    {
        CreatureSystems creatures = world.CreateSystems();
        var probes = new B5Probes(
            creatures,
            new PactOfferSystem(world.World, world.Services.Bonding, world.Services.Resolver, world.Data.Species[species], PactTerms.Of(0, 5)),
            new ChurnSystem(scenario, world, world.Reputation, EventId(world, "KilledKin"), species));

        scheduler.Register(creatures.JobApply);
        scheduler.Register(new PlayerRouteSystem(world.World, regionSize, playerSpeed));
        scheduler.Register(creatures.Lod);
        scheduler.Register(creatures.Perception);
        scheduler.Register(creatures.Decision);
        scheduler.Register(new CrowdReactionSystem(world.World, world.Events, world.Minds, world.Appraisal, EventId(world, "PlaceDanger")));
        scheduler.Register(creatures.Behaviour);
        scheduler.Register(probes.Pacts);
        scheduler.Register(creatures.Intents);
        scheduler.Register(creatures.Movement);
        scheduler.Register(creatures.Separation);
        scheduler.Register(probes.Churn);
        scheduler.Register(creatures.ReputationFlush);
        return probes;
    }

    private static ushort EventId(CreatureWorld world, string key) =>
        world.Rules.FindByKey(key) ?? throw new InvalidOperationException($"Shipped data has no memory event '{key}'.");
}
