using System.Numerics;
using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Cognition.Behaviour;
using SumonerWorld.Core.Cognition.Decision;
using SumonerWorld.Core.Cognition.Lod;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Cognition.Opinions;
using SumonerWorld.Core.Cognition.Perception;
using SumonerWorld.Core.Data;
using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Core.Ecosystem;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Events;
using SumonerWorld.Core.Jobs;
using SumonerWorld.Core.Navigation;
using SumonerWorld.Core.Randomness;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.Spatial;

namespace SumonerWorld.Core.World;

/// <summary>Size and limits of a creature world (from server config, R48).</summary>
public sealed class CreatureWorldSettings
{
    /// <summary>South-west corner of the playable area.</summary>
    public Vector3 BoundsMin { get; init; }

    /// <summary>North-east corner of the playable area.</summary>
    public Vector3 BoundsMax { get; init; } = new(512, 0, 512);

    /// <summary>Walking speed in metres per tick at 100% (behaviour trees scale it).</summary>
    public float WalkSpeed { get; init; } = 0.1f;

    /// <summary>Most entities (creatures + avatars) at once.</summary>
    public int EntityCapacity { get; init; } = 256;

    /// <summary>Background job threads (0 = inline); results do not depend on it.</summary>
    public int JobWorkers { get; init; }

    /// <summary>Edge of a reputation region, in metres (AI Bible §4.5).</summary>
    public int RegionCellSize { get; init; } = 256;

    /// <summary>Behaviour-tree intents that can be queued in one tick.</summary>
    public int IntentCapacity { get; init; } = 1024;

    /// <summary>The terrain everything walks on (TDD §5.1); null = flat ground at each entity's own height.</summary>
    public Heightfield? Ground { get; init; }

    /// <summary>Path finding (TDD §2.4); null = straight lines.</summary>
    public INavigation? Navigation { get; init; }
}

/// <summary>The AI services shared by the creature systems.</summary>
/// <param name="Lod">AI level of detail.</param>
/// <param name="Resolver">Entity → memory subject.</param>
/// <param name="Evaluator">Utility AI.</param>
/// <param name="Runner">Behaviour trees.</param>
/// <param name="Bonding">Bonding Contract.</param>
public sealed record CreatureServices(AiLodPolicy Lod, CreatureSubjectResolver Resolver, UtilityEvaluator Evaluator, BehaviourTreeRunner Runner, BondingRules Bonding);

/// <summary>The creature simulation systems, in standard tick order (see <see cref="RegisterAll"/>).</summary>
/// <param name="JobApply">Applies background job results.</param>
/// <param name="Lod">AI tiers.</param>
/// <param name="Perception">Senses, focus, rumours.</param>
/// <param name="Decision">Utility AI.</param>
/// <param name="Behaviour">Behaviour trees.</param>
/// <param name="Intents">Creature intents.</param>
/// <param name="Movement">Movement.</param>
/// <param name="ReputationFlush">Reputation aggregation.</param>
public sealed record CreatureSystems(
    JobApplySystem JobApply,
    LodSystem Lod,
    PerceptionSystem Perception,
    DecisionSystem Decision,
    BehaviourSystem Behaviour,
    CreatureIntentSystem Intents,
    MovementSystem Movement,
    ReputationFlushSystem ReputationFlush)
{
    /// <summary>Builds the systems for <paramref name="world"/> (wires the path planner to the movement system).</summary>
    public static CreatureSystems Create(CreatureWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        (PerceptionSystem perception, DecisionSystem decision) = MindSystems(world);
        (CreatureIntentSystem intents, MovementSystem movement) = BodySystems(world);
        return new CreatureSystems(
            new JobApplySystem(world.Jobs),
            new LodSystem(world.World, world.Services.Lod),
            perception,
            decision,
            new BehaviourSystem(world.World, world.Services.Runner, world.Services.Lod, world.Services.Resolver),
            intents,
            movement,
            new ReputationFlushSystem(world.Reputation));
    }

    private static (PerceptionSystem Perception, DecisionSystem Decision) MindSystems(CreatureWorld world)
    {
        CreatureServices services = world.Services;
        GoalSet[] goals = [.. world.Data.Species.Select(services.Evaluator.ForSpecies)];
        return (
            new PerceptionSystem(world.World, new Perceiver(world.Grid, world.Opinions), services.Lod, services.Resolver, world.Appraisal, world.Minds, world.Events, world.Data.Species),
            new DecisionSystem(world.World, services.Evaluator, services.Lod, services.Resolver, goals, world.Random));
    }

    private static (CreatureIntentSystem Intents, MovementSystem Movement) BodySystems(CreatureWorld world)
    {
        CreatureWorldSettings settings = world.Settings;
        var movement = new MovementSystem(world.World, world.Grid, world.Services.Runner, settings.Ground, settings.EntityCapacity);
        world.Paths.Sink = movement;
        var intents = new CreatureIntentSystem(
            world.World, world.Events, world.Services.Runner, world.Minds, world.Random, world.Paths, world.Jobs, settings.WalkSpeed, settings.BoundsMin, settings.BoundsMax);
        return (intents, movement);
    }

    /// <summary>Registers every system with the scheduler (hosts that add their own systems may register individually instead).</summary>
    public void RegisterAll(SimulationScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        scheduler.Register(JobApply);
        scheduler.Register(Lod);
        scheduler.Register(Perception);
        scheduler.Register(Decision);
        scheduler.Register(Behaviour);
        scheduler.Register(Intents);
        scheduler.Register(Movement);
        scheduler.Register(ReputationFlush);
    }
}

/// <summary>
/// A world of creatures with full minds (AI Bible) on the server (R30): the ECS world, spatial grid, mind store, opinions,
/// appraisal, utility AI, behaviour trees, bonding, reputation and background jobs, built from the game data. Used by the dedicated
/// server and by the benchmark scenarios, so both run exactly the same simulation (R47).
/// </summary>
public sealed class CreatureWorld : IDisposable
{
    private const int NeighbourEventCapacity = 256;
    private const int JobCapacity = 16;
    private const int ReputationCapacity = 256;

    private readonly CreatureWorldSettings _settings;
    private readonly StableIdAllocator _ids;
    private readonly Dictionary<StableId, Entity> _creaturesById;
    private readonly int _regionsPerRow;

    /// <summary>Builds an empty world from the game data.</summary>
    public CreatureWorld(DataRegistry data, CreatureWorldSettings settings, ulong seed)
        : this(data, settings, seed, startTick: 0, lastStableId: 0)
    {
    }

    private CreatureWorld(DataRegistry data, CreatureWorldSettings settings, ulong randomState, ulong startTick, ulong lastStableId)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;
        _ids = new StableIdAllocator(lastStableId);
        _creaturesById = new Dictionary<StableId, Entity>(settings.EntityCapacity);
        Data = data;
        Random = new SeededRandom(randomState);
        World = CreateEcsWorld(settings.EntityCapacity);
        Rules = new MemoryRules(data.MemoryEventTypes, data.OpinionWeights, SimulationSettings.DefaultTicksPerDay);
        Minds = new CreatureMindStore(Rules, settings.EntityCapacity);
        Opinions = new OpinionModel(Minds, Rules);
        Appraisal = new AppraisalModel(Minds, Opinions, Rules, AppraisalSettings.Default);
        Vector3 size = settings.BoundsMax - settings.BoundsMin;
        Grid = new SpatialGrid(new SpatialGridSettings(
            settings.BoundsMin.X, settings.BoundsMin.Z, size.X, size.Z, SpatialGridSettings.DefaultCellSize, settings.EntityCapacity));
        Jobs = new JobScheduler(settings.JobWorkers, JobCapacity);
        Reputation = new ReputationService(Rules, Jobs, ReputationCapacity, ReputationCapacity);
        Events = new EventBus();
        Events.Register<NeighboursSensed>(NeighbourEventCapacity);
        Events.Register<CreatureIntent>(settings.IntentCapacity);
        Clock = new SimulationClock(SimulationSettings.Default, startTick);
        Services = CreateServices();
        Paths = new PathPlanner(settings.Navigation ?? StraightLineNavigation.Instance, World, settings.EntityCapacity);
        _regionsPerRow = Math.Max(1, (int)MathF.Ceiling(size.X / settings.RegionCellSize));
    }

    /// <summary>The game data.</summary>
    public DataRegistry Data { get; }

    /// <summary>The world's random stream (seeded).</summary>
    public SeededRandom Random { get; }

    /// <summary>Entities and components.</summary>
    public EcsWorld World { get; }

    /// <summary>Memory event rules.</summary>
    public MemoryRules Rules { get; }

    /// <summary>Creature minds.</summary>
    public CreatureMindStore Minds { get; }

    /// <summary>Opinions.</summary>
    public OpinionModel Opinions { get; }

    /// <summary>Emotions and emotes.</summary>
    public AppraisalModel Appraisal { get; }

    /// <summary>Spatial index of all bodies.</summary>
    public SpatialGrid Grid { get; }

    /// <summary>Background jobs.</summary>
    public JobScheduler Jobs { get; }

    /// <summary>Species reputation.</summary>
    public ReputationService Reputation { get; }

    /// <summary>Per-tick events.</summary>
    public EventBus Events { get; }

    /// <summary>World clock.</summary>
    public SimulationClock Clock { get; }

    /// <summary>AI services.</summary>
    public CreatureServices Services { get; }

    /// <summary>Asynchronous path requests (results at the next tick's job stage).</summary>
    public PathPlanner Paths { get; }

    /// <summary>The settings the world was built with.</summary>
    public CreatureWorldSettings Settings => _settings;

    /// <summary>The playable area's south-west corner.</summary>
    public Vector3 BoundsMin => _settings.BoundsMin;

    /// <summary>The playable area's north-east corner.</summary>
    public Vector3 BoundsMax => _settings.BoundsMax;

    /// <summary>Index of a species in the data registry (what <see cref="SpeciesRef"/> holds).</summary>
    public ushort SpeciesIndex(string speciesId)
    {
        for (int i = 0; i < Data.Species.Count; i++)
        {
            if (Data.Species[i].Id == speciesId)
            {
                return (ushort)i;
            }
        }

        throw new KeyNotFoundException($"Unknown species '{speciesId}'.");
    }

    /// <summary>The reputation region of a position (AI Bible §4.5).</summary>
    public int RegionOf(Vector3 position)
    {
        Vector3 local = position - _settings.BoundsMin;
        return ((int)(local.Z / _settings.RegionCellSize) * _regionsPerRow) + (int)(local.X / _settings.RegionCellSize);
    }

    /// <summary>Creates the creature systems (register them with <see cref="CreatureSystems.RegisterAll"/> or one by one).</summary>
    public CreatureSystems CreateSystems()
    {
        return CreatureSystems.Create(this);
    }

    /// <summary>
    /// Adds a wild creature with a fresh mind; its kind's opinion of players known in the region is seeded from reputation
    /// (AI Bible §4.5). Needs start at 0.
    /// </summary>
    public Entity SpawnCreature(ushort species, Vector3 position, Personality personality, int group)
    {
        Entity creature = World.Create();
        StableId id = _ids.Allocate();
        World.Add(creature, id);
        _creaturesById.Add(id, creature);
        AddBody(creature, position);
        World.Add(creature, new CurrentEmote(Emote.None));
        World.Add(creature, new CurrentGoal(Decision.Idle));
        World.Add(creature, new AiTier(AiLodTier.Far));
        World.Add(creature, default(FocusRef));
        MindHandle mind = Minds.Allocate(personality, Data.Species[species].Needs);
        World.Add(creature, new MindRef(mind));
        World.Add(creature, new GroupRef(group));
        World.Add(creature, new SpeciesRef(species));
        Reputation.SeedOpinions(Minds, mind, species, RegionOf(position));
        return creature;
    }

    /// <summary>Removes a creature and frees its mind.</summary>
    public void DespawnCreature(Entity creature)
    {
        Grid.Remove(creature);
        _creaturesById.Remove(World.GetComponent<StableId>(creature));
        Minds.Release(World.GetComponent<MindRef>(creature).Handle);
        World.Destroy(creature);
    }

    /// <summary>The live creature with a stable id (what clients name creatures by), if any.</summary>
    public bool TryFindCreature(StableId id, out Entity creature) => _creaturesById.TryGetValue(id, out creature);

    /// <summary>Adds a player's avatar (what creatures perceive and remember the player by).</summary>
    public Entity SpawnPlayerAvatar(ulong accountId, Vector3 position)
    {
        Entity avatar = World.Create();
        World.Add(avatar, new PlayerAvatar(accountId));
        AddBody(avatar, position);
        return avatar;
    }

    /// <summary>Removes a player's avatar.</summary>
    public void DespawnPlayerAvatar(Entity avatar)
    {
        Grid.Remove(avatar);
        World.Destroy(avatar);
    }

    /// <summary>Last stable id issued (saved with the world).</summary>
    internal ulong LastStableId => _ids.LastIssued;

    /// <summary>An empty world that continues a saved clock, random stream and id counter (see <see cref="WorldSnapshots"/>).</summary>
    internal static CreatureWorld Continue(DataRegistry data, CreatureWorldSettings settings, ulong randomState, ulong tick, ulong lastStableId) =>
        new(data, settings, randomState, tick, lastStableId);

    /// <summary>Adds all deterministic world state to a hash (TDD §2.3).</summary>
    public void AddTo(ref StateHash hash, BondingRules bonding)
    {
        ArgumentNullException.ThrowIfNull(bonding);
        hash.Add(Clock.Current.Index);
        hash.Add<Entity>(World.Store<Position>().Entities);
        hash.Add<StableId>(World.Store<StableId>().Components);
        hash.Add<Position>(World.Store<Position>().Components);
        hash.Add<Facing>(World.Store<Facing>().Components);
        hash.Add<MoveTarget>(World.Store<MoveTarget>().Components);
        hash.Add<AiTier>(World.Store<AiTier>().Components);
        hash.Add<FocusRef>(World.Store<FocusRef>().Components);
        hash.Add<CurrentEmote>(World.Store<CurrentEmote>().Components);
        hash.Add<CurrentGoal>(World.Store<CurrentGoal>().Components);
        hash.Add<GroupRef>(World.Store<GroupRef>().Components);
        Minds.AddTo(ref hash);
        bonding.AddTo(ref hash);
        Reputation.AddTo(ref hash);
    }

    /// <summary>Stops the background job threads.</summary>
    public void Dispose() => Jobs.Dispose();

    private static EcsWorld CreateEcsWorld(int capacity)
    {
        var world = new EcsWorld(capacity);
        world.Register<StableId>();
        world.Register<Position>();
        world.Register<Facing>();
        world.Register<MoveTarget>();
        world.Register<MindRef>();
        world.Register<CurrentEmote>();
        world.Register<CurrentGoal>();
        world.Register<AiTier>();
        world.Register<FocusRef>();
        world.Register<GroupRef>();
        world.Register<SpeciesRef>();
        world.Register<PlayerAvatar>();
        return world;
    }

    private CreatureServices CreateServices()
    {
        var sampler = new InputSampler(Minds, Appraisal, Opinions, SimulationSettings.DefaultTicksPerDay);
        return new CreatureServices(
            new AiLodPolicy(AiLodSettings.Default),
            new CreatureSubjectResolver(World),
            new UtilityEvaluator(Minds, Appraisal, Opinions, Data.Goals, SimulationSettings.DefaultTicksPerDay),
            new BehaviourTreeRunner(Minds, sampler, Events, Data.Goals, Data.BehaviourTrees),
            new BondingRules(
                Minds,
                Opinions,
                new PactEvaluator(Minds, Opinions, Data.PactTerms),
                Rules,
                Data.BondLevels,
                Data.PactTerms.Count,
                SimulationSettings.DefaultTicksPerDay));
    }

    /// <summary>Adds a creature with a saved id and mind (see <see cref="WorldSnapshots"/>).</summary>
    internal Entity AddRestoredCreature(StableId id, Vector3 position, MindHandle mind)
    {
        Entity creature = World.Create();
        World.Add(creature, id);
        _creaturesById.Add(id, creature);
        AddBody(creature, position);
        World.Add(creature, new MindRef(mind));
        return creature;
    }

    private void AddBody(Entity entity, Vector3 position)
    {
        if (_settings.Ground is Heightfield ground)
        {
            position.Y = ground.HeightAt(position.X, position.Z);
        }

        World.Add(entity, new Position(position));
        World.Add(entity, new Facing(Vector3.UnitZ));
        World.Add(entity, new MoveTarget(position, 0, 0));
        Grid.Insert(entity, position);
    }
}
