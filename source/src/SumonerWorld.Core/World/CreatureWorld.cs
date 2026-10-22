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
using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Data;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Core.Ecosystem;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Events;
using SumonerWorld.Core.Items;
using SumonerWorld.Core.Jobs;
using SumonerWorld.Core.Navigation;
using SumonerWorld.Core.Progression;
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
/// <param name="PactAnswers">Creatures answering a Pact offer with their body (M2-05), in place of their trees.</param>
/// <param name="Companions">Summoned companions following, defending and roaming (M2-07), in place of their trees.</param>
/// <param name="Intents">Creature intents.</param>
/// <param name="Movement">Movement.</param>
/// <param name="Separation">Keeps bodies apart after movement.</param>
/// <param name="Summoning">Summoning sigils finishing: creatures arrive at their summoner's side (M2-06).</param>
/// <param name="Fight">Attacks, hits, companion alerts, status effects and vitals.</param>
/// <param name="Growth">Companion experience, levels and life stages (M2-09).</param>
/// <param name="Skills">Skill upkeep: picking unspent points, reluctance (M2-10).</param>
/// <param name="ReputationFlush">Reputation aggregation.</param>
/// <param name="Quests">Quest objectives and rewards (M2-19).</param>
/// <param name="Arena">The Academy's arena: the training construct and the practical moments (M2-21b).</param>
public sealed record CreatureSystems(
    JobApplySystem JobApply,
    LodSystem Lod,
    PerceptionSystem Perception,
    DecisionSystem Decision,
    BehaviourSystem Behaviour,
    PactAnswerSystem PactAnswers,
    CompanionSystem Companions,
    CreatureIntentSystem Intents,
    MovementSystem Movement,
    SeparationSystem Separation,
    SummoningSystem Summoning,
    FightSystems Fight,
    GrowthSystem Growth,
    SkillSystem Skills,
    ReputationFlushSystem ReputationFlush,
    Quests.QuestSystem Quests,
    Academy.ArenaSystem Arena)
{
    /// <summary>Builds the systems for <paramref name="world"/> (wires the path planner to the movement system).</summary>
    public static CreatureSystems Create(CreatureWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        (PerceptionSystem perception, DecisionSystem decision) = MindSystems(world);
        BodySizes sizes = world.Sizes;
        (CreatureIntentSystem intents, MovementSystem movement) = BodySystems(world, sizes);
        return new CreatureSystems(
            new JobApplySystem(world.Jobs),
            new LodSystem(world.World, world.Services.Lod, world.Services.Bonding),
            perception,
            decision,
            new BehaviourSystem(world.World, world.Services.Runner, world.Services.Lod, world.Services.Resolver),
            new PactAnswerSystem(world),
            new CompanionSystem(world),
            intents,
            movement,
            new SeparationSystem(world.World, world.Grid, sizes, world.Settings.Ground),
            new SummoningSystem(world),
            FightSystems.Create(world),
            new GrowthSystem(world),
            new SkillSystem(world),
            new ReputationFlushSystem(world.Reputation),
            new Quests.QuestSystem(world),
            new Academy.ArenaSystem(world));
    }

    /// <summary>Creatures' attack intents (M2-03).</summary>
    public CreatureAttackSystem Attacks => Fight.Attacks;

    /// <summary>Moves and hits (combat spec §4).</summary>
    public CombatSystem Combat => Fight.Combat;

    /// <summary>Regeneration, yield, defeat (combat spec §5, §6).</summary>
    public VitalsSystem Vitals => Fight.Vitals;

    private static (PerceptionSystem Perception, DecisionSystem Decision) MindSystems(CreatureWorld world)
    {
        CreatureServices services = world.Services;
        GoalSet[] goals = [.. world.Data.Species.Select(services.Evaluator.ForSpecies)];
        return (
            new PerceptionSystem(world.World, new Perceiver(world.Grid, world.Opinions), services.Lod, services.Resolver, world.Appraisal, world.Minds, world.Events, world.Data.Species),
            new DecisionSystem(world.World, services.Evaluator, services.Lod, services.Resolver, goals, world.Random));
    }

    private static (CreatureIntentSystem Intents, MovementSystem Movement) BodySystems(CreatureWorld world, BodySizes sizes)
    {
        CreatureWorldSettings settings = world.Settings;
        var movement = new MovementSystem(world.World, world.Grid, world.Services.Runner, settings.Ground, settings.EntityCapacity);
        world.Paths.Sink = movement;
        var intents = new CreatureIntentSystem(
            world.World, world.Events, world.Services.Runner, world.Minds, world.Random, world.Paths, world.Jobs, sizes, settings.WalkSpeed, settings.BoundsMin, settings.BoundsMax);
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
        scheduler.Register(PactAnswers);
        scheduler.Register(Companions);
        scheduler.Register(Intents);
        scheduler.Register(Movement);
        scheduler.Register(Separation);
        scheduler.Register(Summoning);
        scheduler.Register(Fight.Attacks);
        scheduler.Register(Fight.Combat);
        scheduler.Register(Fight.Alerts);
        scheduler.Register(Fight.Combos);
        scheduler.Register(Fight.Status);
        scheduler.Register(Fight.Vitals);
        scheduler.Register(Fight.Loot);
        scheduler.Register(Fight.Crafting);
        scheduler.Register(Quests);
        scheduler.Register(Arena);
        scheduler.Register(Growth);
        scheduler.Register(Skills);
        scheduler.Register(ReputationFlush);
    }
}

/// <summary>The fight systems, in tick order (combat spec §4–§7).</summary>
/// <param name="Attacks">Creatures' attack intents (M2-03).</param>
/// <param name="Combat">Moves and hits (combat spec §4).</param>
/// <param name="Alerts">Hits that give summoned companions a foe (M2-07).</param>
/// <param name="Combos">Combo learning and companions starting a known combo by themselves (M2-12).</param>
/// <param name="Status">Status effects over time: mending, expiry (M2-11).</param>
/// <param name="Vitals">Regeneration, yield, defeat (combat spec §5, §6).</param>
/// <param name="Loot">Personal loot bags from defeated creatures (M2-13).</param>
/// <param name="Crafting">Finishing crafts, or interrupting them on a hit (M2-14).</param>
public sealed record FightSystems(
    CreatureAttackSystem Attacks,
    CombatSystem Combat,
    CompanionAlertSystem Alerts,
    ComboSystem Combos,
    StatusSystem Status,
    VitalsSystem Vitals,
    LootSystem Loot,
    CraftingSystem Crafting)
{
    /// <summary>Builds them for a world.</summary>
    public static FightSystems Create(CreatureWorld world) =>
        new(
            new CreatureAttackSystem(world),
            new CombatSystem(world),
            new CompanionAlertSystem(world),
            new ComboSystem(world),
            new StatusSystem(world.World),
            new VitalsSystem(world),
            new LootSystem(world),
            new CraftingSystem(world));
}

/// <summary>
/// A world of creatures with full minds (AI Bible) on the server (R30): the ECS world, spatial grid, mind store, opinions,
/// appraisal, utility AI, behaviour trees, bonding, reputation and background jobs, built from the game data. Used by the dedicated
/// server and by the benchmark scenarios, so both run exactly the same simulation (R47).
/// </summary>
public sealed class CreatureWorld : IDisposable
{
    private const int NeighbourEventCapacity = 256;

    private GrowthRules? _growth;
    private Economy? _economy;
    private const int JobCapacity = 16;
    private const int ReputationCapacity = 256;
    private const int InteractionEventCapacity = 256;
    private const int CombatEventCapacity = 512;
    private const int ScaredFear = 60;

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
        Events.Register<PlayerInteracted>(InteractionEventCapacity);
        Economy.RegisterEvents(Events, InteractionEventCapacity);
        Events.Register<MemoryFormed>(InteractionEventCapacity);
        Events.Register<CombatHit>(CombatEventCapacity);
        Events.Register<CombatantDefeated>(InteractionEventCapacity);
        Clock = new SimulationClock(SimulationSettings.Default, startTick);
        Services = CreateServices();
        Paths = new PathPlanner(settings.Navigation ?? StraightLineNavigation.Instance, World, settings.EntityCapacity);
        Sizes = new BodySizes(World, data.Species);
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

    /// <summary>Inventories, loot bags and gathering nodes (M2-13).</summary>
    public Economy Economy => _economy ??= new Economy();

    /// <summary>How creatures grow (companions spec §3–4, M2-09).</summary>
    public GrowthRules Growth => _growth ??= new GrowthRules(Data.Combat.Growth);

    /// <summary>Body footprint sizes.</summary>
    public BodySizes Sizes { get; }

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
        ProgressionComponents.AddWild(World, creature, Data.Species[species], id.Value);
        AddCombatant(creature, species);
        Reputation.SeedOpinions(Minds, mind, species, RegionOf(position));
        return creature;
    }

    /// <summary>Catches a creature in a snare: it stops, shows fear and cannot move until <see cref="Release"/> (M1-23 Memory Grove).</summary>
    public void Snare(Entity creature, ulong now)
    {
        if (!World.Has<Snared>(creature))
        {
            World.Add(creature, new Snared(now));
        }

        World.GetComponent<MoveTarget>(creature) = World.GetComponent<MoveTarget>(creature) with { Active = 0 };
        World.GetComponent<CurrentEmote>(creature) = new CurrentEmote(Emote.Fear);
    }

    /// <summary>Frees a snared creature; false if it was not snared. Its next perception sets the emote it shows.</summary>
    public bool Release(Entity creature)
    {
        // The fear stays on show until perception appraises its focus again (no flicker through the tree's emote).
        return World.Remove<Snared>(creature);
    }

    /// <summary>
    /// Every other creature of <paramref name="species"/> within <see cref="PlayerInteractions.WitnessRadius"/> of the victim
    /// remembers <c>KilledKin</c> about the player; returns how many.
    /// </summary>
    public int WitnessKill(Entity victim, ushort species, ulong account, ulong now)
    {
        Span<Entity> near = stackalloc Entity[PlayerInteractions.MaxAffected];
        int found = Grid.QueryRadius(World.GetComponent<Position>(victim).Value, PlayerInteractions.WitnessRadius, near).Count;
        int witnesses = 0;
        for (int i = 0; i < found; i++)
        {
            if (near[i] != victim && World.Has<SpeciesRef>(near[i]) && World.GetComponent<SpeciesRef>(near[i]).Index == species)
            {
                PlayerInteractions.Remember(this, near[i], "KilledKin", account, now);
                witnesses++;
            }
        }

        return witnesses;
    }

    /// <summary>
    /// Every predator within <see cref="PlayerInteractions.ScareRadius"/> of the avatar remembers <c>AttackedBy</c> and is
    /// frightened; returns how many and the first one's species (<see cref="ushort.MaxValue"/> when none).
    /// </summary>
    public (int Scared, ushort Species) ScarePredators(Entity avatar, ulong account, ulong now)
    {
        Span<Entity> near = stackalloc Entity[PlayerInteractions.MaxAffected];
        int found = Grid.QueryRadius(World.GetComponent<Position>(avatar).Value, PlayerInteractions.ScareRadius, near).Count;
        int scared = 0;
        ushort first = ushort.MaxValue;
        for (int i = 0; i < found; i++)
        {
            if (!World.Has<SpeciesRef>(near[i]) || Data.Species[World.GetComponent<SpeciesRef>(near[i]).Index].Role != CreatureRole.Predator)
            {
                continue;
            }

            PlayerInteractions.Remember(this, near[i], "AttackedBy", account, now);
            Appraisal.Stimulate(World.GetComponent<MindRef>(near[i]).Handle, Emotion.Fear, ScaredFear, now);
            first = scared == 0 ? World.GetComponent<SpeciesRef>(near[i]).Index : first;
            scared++;
        }

        return (scared, first);
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
        World.Add(avatar, CombatRules.Create(Data.Combat.Player, CombatRules.PlayerSource));
        ProgressionComponents.AddPlayer(World, avatar);
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
        hash.Add<Entity>(World.Store<Combatant>().Entities);
        hash.Add<Combatant>(World.Store<Combatant>().Components);
        ComponentSets.AddTo(ref hash, World);
        Economy.AddTo(ref hash);
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
        world.Register<Snared>();
        ComponentSets.Register(world);
        world.Register<Combatant>();
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

    /// <summary>Gives a creature its species' combat stats at full health (health is not saved yet: M2-15).</summary>
    internal void AddCombatant(Entity creature, ushort species)
    {
        Combatant combatant = CombatRules.Create(Data.Species[species].Combat, species);
        ProgressionComponents.Restat(Growth, Data, World, creature, Data.Species[species].Combat, ref combatant);
        World.Add(creature, combatant);
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
