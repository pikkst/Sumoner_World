using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Cognition.Behaviour;
using SumonerWorld.Core.Cognition.Decision;
using SumonerWorld.Core.Cognition.Lod;
using SumonerWorld.Core.Cognition.Perception;
using SumonerWorld.Core.Jobs;
using SumonerWorld.Core.Simulation;

namespace SumonerWorld.Scenarios.B5;

/// <summary>Systems whose counters the scenario reports, and the bonding rules it hashes.</summary>
internal sealed record B5Probes(PerceptionSystem Perception, IntentSystem Intents, PactOfferSystem Pacts, ChurnSystem Churn, BondingRules Bonding);

/// <summary>The AI services shared by the B5 systems.</summary>
internal sealed record B5Ai(AiLodPolicy Lod, B5SubjectResolver Resolver, UtilityEvaluator Evaluator, BehaviourTreeRunner Runner, BondingRules Bonding);

/// <summary>Builds the B5 systems and registers them with the scheduler in tick order.</summary>
internal static class B5Pipeline
{
    public static B5Probes Register(B5World b5, SimulationScheduler scheduler, B5Scenario scenario)
    {
        B5Ai ai = BuildAi(b5);
        B5Probes probes = BuildProbes(b5, ai, scenario);

        scheduler.Register(new JobApplySystem(b5.Jobs));
        scheduler.Register(new PlayerRouteSystem(b5.World, B5World.RegionSize, B5World.PlayerSpeed));
        RegisterAi(b5, scheduler, ai, probes);
        scheduler.Register(probes.Intents);
        scheduler.Register(new MovementSystem(b5.World, b5.Grid, ai.Runner));
        scheduler.Register(probes.Churn);
        scheduler.Register(new ReputationFlushSystem(b5.Reputation));
        return probes;
    }

    private static B5Probes BuildProbes(B5World b5, B5Ai ai, B5Scenario scenario) => new(
        new PerceptionSystem(b5.World, new Perceiver(b5.Grid, b5.Opinions), ai.Lod, ai.Resolver, b5.Appraisal, b5.Events, b5.Species.Perception)
        {
            Minds = b5.Minds,
        },
        new IntentSystem(b5.World, b5.Events, ai.Runner, b5.Minds, b5.Random, B5World.WalkSpeed, B5World.RegionSize),
        new PactOfferSystem(b5.World, ai.Bonding, ai.Resolver, b5.Species, PactTerms.Of(0, 5)),
        new ChurnSystem(scenario, b5.World, b5.Reputation, b5.EventId("KilledKin")),
        ai.Bonding);

    private static void RegisterAi(B5World b5, SimulationScheduler scheduler, B5Ai ai, B5Probes probes)
    {
        scheduler.Register(new LodSystem(b5.World, ai.Lod));
        scheduler.Register(probes.Perception);
        scheduler.Register(new DecisionSystem(b5.World, ai.Evaluator, ai.Lod, ai.Resolver, ai.Evaluator.ForSpecies(b5.Species), b5.Random));
        scheduler.Register(new CrowdReactionSystem(b5.World, b5.Events, b5.Minds, b5.Appraisal, b5.EventId("PlaceDanger")));
        scheduler.Register(new BehaviourSystem(b5.World, ai.Runner, ai.Lod, ai.Resolver));
        scheduler.Register(probes.Pacts);
    }

    private static B5Ai BuildAi(B5World b5)
    {
        var sampler = new InputSampler(b5.Minds, b5.Appraisal, b5.Opinions, SimulationSettings.DefaultTicksPerDay);
        return new B5Ai(
            new AiLodPolicy(AiLodSettings.Default),
            new B5SubjectResolver(b5.World),
            new UtilityEvaluator(b5.Minds, b5.Appraisal, b5.Opinions, b5.Data.Goals, SimulationSettings.DefaultTicksPerDay),
            new BehaviourTreeRunner(b5.Minds, sampler, b5.Events, b5.Data.Goals, b5.Data.BehaviourTrees),
            new BondingRules(
                b5.Minds,
                b5.Opinions,
                new PactEvaluator(b5.Minds, b5.Opinions, b5.Data.PactTerms),
                b5.Rules,
                b5.Data.BondLevels,
                b5.Data.PactTerms.Count,
                SimulationSettings.DefaultTicksPerDay));
    }
}
