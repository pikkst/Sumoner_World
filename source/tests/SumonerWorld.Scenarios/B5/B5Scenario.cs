using System.Numerics;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Cognition.Behaviour;
using SumonerWorld.Core.Cognition.Decision;
using SumonerWorld.Core.Cognition.Lod;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Cognition.Opinions;
using SumonerWorld.Core.Cognition.Perception;
using SumonerWorld.Core.Data;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Events;
using SumonerWorld.Core.Randomness;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.Spatial;

namespace SumonerWorld.Scenarios.B5;

/// <summary>
/// Benchmark scenario B5 "Sim-Only" (M0-07 A1): 200 creatures and 2 stand-in players in the 512 m M1 test region, simulated by
/// Core without Godot. Creatures (species <c>mossback</c> from the shipped data) get an AI tier from their distance to the players
/// (M1-10), perceive neighbours and players at their tier's rate and focus on the most notable one, choose goals with the utility
/// AI (M1-08), carry them out with the shipped behaviour trees (M1-09), remember crowded spots as dangerous (M1-06), react with
/// emotions and emotes (M1-07) and are replaced by churn. Each new AI system is added here so the gates keep covering it.
/// Used by the CI gates (0 allocations per tick, identical runs) and by the benchmark tool (timings).
/// </summary>
public sealed class B5Scenario
{
    /// <summary>Creatures alive at any time.</summary>
    public const int CreatureCount = 200;

    /// <summary>Stand-in players walking a fixed route.</summary>
    public const int PlayerCount = 2;

    /// <summary>Warm-up: 30 s at 30 Hz (M0-07 A4).</summary>
    public const int WarmUpTicks = 900;

    /// <summary>Measured run: 5 min at 30 Hz (M0-07 A1).</summary>
    public const int MeasuredTicks = 9_000;

    private const float RegionSize = 512f;
    private const float Speed = 0.1f;
    private const float PlayerSpeed = 0.15f;
    private const int EntityCapacity = 256;
    private const int EventCapacity = 256;
    private const int IntentCapacity = 1024;
    private const string SpeciesId = "mossback";
    private const ulong FirstAccountId = 9_000_001;

    private readonly EcsWorld _world;
    private readonly SpatialGrid _grid;
    private readonly SeededRandom _random;
    private readonly StableIdAllocator _ids = new();
    private readonly CreatureMindStore _minds;
    private readonly AppraisalModel _appraisal;
    private readonly OpinionModel _opinions;
    private readonly SpeciesDefinition _species;
    private readonly SimulationClock _clock;
    private readonly SimulationScheduler _scheduler;
    private IntentSystem? _intents;
    private PerceptionSystem? _perception;

    /// <summary>Builds the scenario from a seed; <paramref name="metrics"/> optionally receives stage timings.</summary>
    public B5Scenario(ulong seed, IMetrics? metrics = null)
    {
        _random = new SeededRandom(seed);
        _world = CreateWorld();
        DataRegistry data = ShippedData.Current;
        _species = data.GetSpecies(SpeciesId);
        var rules = new MemoryRules(data.MemoryEventTypes, data.OpinionWeights, SimulationSettings.DefaultTicksPerDay);
        _minds = new CreatureMindStore(rules, EntityCapacity);
        _opinions = new OpinionModel(_minds, rules);
        _appraisal = new AppraisalModel(_minds, _opinions, rules, AppraisalSettings.Default);
        _grid = new SpatialGrid(new SpatialGridSettings(0, 0, RegionSize, RegionSize, SpatialGridSettings.DefaultCellSize, EntityCapacity));

        Events = new EventBus();
        Events.Register<NeighbourCount>(EventCapacity);
        Events.Register<CreatureIntent>(IntentCapacity);
        _clock = new SimulationClock(SimulationSettings.Default);
        _scheduler = new SimulationScheduler(_clock, Events, metrics);
        RegisterSystems(rules, data);

        for (int i = 0; i < PlayerCount; i++)
        {
            SpawnPlayer(i);
        }

        for (int i = 0; i < CreatureCount; i++)
        {
            Spawn();
        }
    }

    /// <summary>The scenario's event bus (its growth counter shows hidden allocations).</summary>
    public EventBus Events { get; }

    /// <summary>Creatures currently alive (players not counted).</summary>
    public int LiveCreatures => _world.Store<MindRef>().Count;

    /// <summary>Minds currently allocated.</summary>
    public int LiveMinds => _minds.Count;

    /// <summary>How many creatures currently pursue each goal id (for the gate test).</summary>
    public IReadOnlyDictionary<ushort, int> GoalCounts => Count(_world.Store<CurrentGoal>().Components, goal => goal.Goal);

    /// <summary>How many creatures are in each AI tier right now.</summary>
    public IReadOnlyDictionary<AiLodTier, int> TierCounts => Count(_world.Store<AiTier>().Components, tier => tier.Tier);

    /// <summary>Creatures currently showing an emote.</summary>
    public int CreaturesShowingAnEmote => _world.Store<CurrentEmote>().Components.ToArray().Count(emote => emote.Value != Emote.None);

    /// <summary>Times a creature chose a player as its focus since the start.</summary>
    public int PlayerFocusEvents => _perception?.PlayerFocusEvents ?? 0;

    /// <summary>Interact intents carried out (eat, rest, socialise...) since the start.</summary>
    public int Interactions => _intents?.Interactions ?? 0;

    /// <summary>Memory records currently stored by all creatures.</summary>
    public int StoredMemories => _minds.TotalRecords;

    /// <summary>Simulates one tick.</summary>
    public void RunTick() => _scheduler.RunTick();

    /// <summary>Hash of all deterministic state: ids, positions, facings, targets, tiers, focus, emotes, goals and minds (TDD §2.3).</summary>
    public ulong ComputeStateHash()
    {
        var hash = default(StateHash);
        hash.Add(_clock.Current.Index);
        hash.Add<Entity>(_world.Store<Position>().Entities);
        hash.Add<StableId>(_world.Store<StableId>().Components);
        hash.Add<Position>(_world.Store<Position>().Components);
        hash.Add<Facing>(_world.Store<Facing>().Components);
        hash.Add<MoveTarget>(_world.Store<MoveTarget>().Components);
        hash.Add<AiTier>(_world.Store<AiTier>().Components);
        hash.Add<FocusRef>(_world.Store<FocusRef>().Components);
        hash.Add<CurrentEmote>(_world.Store<CurrentEmote>().Components);
        hash.Add<CurrentGoal>(_world.Store<CurrentGoal>().Components);
        _minds.AddTo(ref hash);
        return hash.Value;
    }

    internal void Spawn()
    {
        Entity creature = _world.Create();
        var position = new Vector3((float)(_random.NextDouble() * RegionSize), 0, (float)(_random.NextDouble() * RegionSize));
        _world.Add(creature, _ids.Allocate());
        AddBody(creature, position);
        _world.Add(creature, new CurrentEmote(Emote.None));
        _world.Add(creature, new CurrentGoal(Decision.Idle));
        _world.Add(creature, new AiTier(AiLodTier.Far));
        _world.Add(creature, default(FocusRef));
        MindHandle mind = _minds.Allocate(Personality.Roll(_species, _random), _species.Needs);
        _world.Add(creature, new MindRef(mind));

        // Creatures enter the world with varied needs, so goals differ from the start.
        _minds.SetNeed(mind, Need.Hunger, _random.NextInt(0, ICreatureMindStore.MaxNeed + 1), _clock.Current.Index);
        _minds.SetNeed(mind, Need.Rest, _random.NextInt(0, ICreatureMindStore.MaxNeed + 1), _clock.Current.Index);
        _minds.SetNeed(mind, Need.Social, _random.NextInt(0, ICreatureMindStore.MaxNeed + 1), _clock.Current.Index);
    }

    internal void Despawn(Entity creature)
    {
        _grid.Remove(creature);
        _minds.Release(_world.GetComponent<MindRef>(creature).Handle);
        _world.Destroy(creature);
    }

    private static Dictionary<TKey, int> Count<T, TKey>(Span<T> values, Func<T, TKey> key)
        where TKey : notnull
    {
        var counts = new Dictionary<TKey, int>();
        foreach (T value in values)
        {
            counts[key(value)] = counts.GetValueOrDefault(key(value)) + 1;
        }

        return counts;
    }

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
        return world;
    }

    private void RegisterSystems(MemoryRules rules, DataRegistry data)
    {
        ushort placeDanger = rules.FindByKey("PlaceDanger") ?? throw new InvalidOperationException("Shipped data has no PlaceDanger memory event.");
        B5Ai ai = BuildAi(data);
        _perception = new PerceptionSystem(_world, new Perceiver(_grid, _opinions), ai.Lod, ai.Resolver, _appraisal, Events, _species.Perception);
        _intents = new IntentSystem(_world, Events, ai.Runner, _minds, _random, Speed, RegionSize);

        _scheduler.Register(new PlayerRouteSystem(_world, RegionSize, PlayerSpeed));
        RegisterAiSystems(ai, placeDanger);
        _scheduler.Register(_intents);
        _scheduler.Register(new MovementSystem(_world, _grid, ai.Runner));
        _scheduler.Register(new ChurnSystem(this, _world));
    }

    private void RegisterAiSystems(B5Ai ai, ushort placeDanger)
    {
        _scheduler.Register(new LodSystem(_world, ai.Lod));
        _scheduler.Register(_perception!);
        _scheduler.Register(new DecisionSystem(_world, ai.Evaluator, ai.Lod, ai.Resolver, ai.Evaluator.ForSpecies(_species), _random));
        _scheduler.Register(new CrowdReactionSystem(_world, Events, _minds, _appraisal, placeDanger));
        _scheduler.Register(new BehaviourSystem(_world, ai.Runner, ai.Lod, ai.Resolver));
    }

    private B5Ai BuildAi(DataRegistry data)
    {
        var sampler = new InputSampler(_minds, _appraisal, _opinions, SimulationSettings.DefaultTicksPerDay);
        return new B5Ai(
            new AiLodPolicy(AiLodSettings.Default),
            new B5SubjectResolver(_world),
            new UtilityEvaluator(_minds, _appraisal, _opinions, data.Goals, SimulationSettings.DefaultTicksPerDay),
            new BehaviourTreeRunner(_minds, sampler, Events, data.Goals, data.BehaviourTrees));
    }

    private void SpawnPlayer(int index)
    {
        Entity player = _world.Create();
        Vector3 start = PlayerRouteSystem.Waypoint(index * 2, RegionSize);
        _world.Add(player, new PlayerTag(FirstAccountId + (ulong)index, index * 2));
        AddBody(player, start);
    }

    private void AddBody(Entity entity, Vector3 position)
    {
        _world.Add(entity, new Position(position));
        _world.Add(entity, new Facing(Vector3.UnitZ));
        _world.Add(entity, new MoveTarget(position, 0, 0));
        _grid.Insert(entity, position);
    }
}

/// <summary>The AI services shared by the B5 systems.</summary>
internal sealed record B5Ai(AiLodPolicy Lod, B5SubjectResolver Resolver, UtilityEvaluator Evaluator, BehaviourTreeRunner Runner);
