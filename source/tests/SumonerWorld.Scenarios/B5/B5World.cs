using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Cognition.Behaviour;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Cognition.Opinions;
using SumonerWorld.Core.Data;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecosystem;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Events;
using SumonerWorld.Core.Jobs;
using SumonerWorld.Core.Randomness;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.Spatial;

namespace SumonerWorld.Scenarios.B5;

/// <summary>The shared state of scenario B5: ECS world, spatial grid, minds, data and services (built once per scenario).</summary>
internal sealed class B5World : IDisposable
{
    public const float RegionSize = 512f;
    public const float WalkSpeed = 0.1f;
    public const float PlayerSpeed = 0.15f;
    public const int EntityCapacity = 256;
    public const string SpeciesId = "mossback";

    private const int EventCapacity = 256;
    private const int IntentCapacity = 1024;
    private const int JobCapacity = 16;
    private const int ReputationCapacity = 256;

    public B5World(ulong seed, int jobWorkers)
    {
        Random = new SeededRandom(seed);
        World = CreateWorld();
        Data = ShippedData.Current;
        Species = Data.GetSpecies(SpeciesId);
        Rules = new MemoryRules(Data.MemoryEventTypes, Data.OpinionWeights, SimulationSettings.DefaultTicksPerDay);
        Minds = new CreatureMindStore(Rules, EntityCapacity);
        Opinions = new OpinionModel(Minds, Rules);
        Appraisal = new AppraisalModel(Minds, Opinions, Rules, AppraisalSettings.Default);
        Grid = new SpatialGrid(new SpatialGridSettings(0, 0, RegionSize, RegionSize, SpatialGridSettings.DefaultCellSize, EntityCapacity));
        Jobs = new JobScheduler(jobWorkers, JobCapacity);
        Reputation = new ReputationService(Rules, Jobs, ReputationCapacity, ReputationCapacity);
        Events = new EventBus();
        Events.Register<NeighbourCount>(EventCapacity);
        Events.Register<CreatureIntent>(IntentCapacity);
        Clock = new SimulationClock(SimulationSettings.Default);
    }

    public SeededRandom Random { get; }

    public EcsWorld World { get; }

    public DataRegistry Data { get; }

    public SpeciesDefinition Species { get; }

    public MemoryRules Rules { get; }

    public CreatureMindStore Minds { get; }

    public OpinionModel Opinions { get; }

    public AppraisalModel Appraisal { get; }

    public SpatialGrid Grid { get; }

    public JobScheduler Jobs { get; }

    public ReputationService Reputation { get; }

    public EventBus Events { get; }

    public SimulationClock Clock { get; }

    public ushort EventId(string key) =>
        Rules.FindByKey(key) ?? throw new InvalidOperationException($"Shipped data has no memory event '{key}'.");

    public void Dispose() => Jobs.Dispose();

    private static EcsWorld CreateWorld()
    {
        var world = new EcsWorld(EntityCapacity);
        world.Register<StableId>();
        world.Register<Position>();
        world.Register<Facing>();
        world.Register<MoveTarget>();
        world.Register<MindRef>();
        world.Register<CurrentEmote>();
        world.Register<CurrentGoal>();
        world.Register<AiTier>();
        world.Register<FocusRef>();
        world.Register<PlayerTag>();
        world.Register<GroupRef>();
        return world;
    }
}
