using System.Numerics;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Cognition.Decision;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Cognition.Opinions;
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
/// Benchmark scenario B5 "Sim-Only" (M0-07 A1): 200 creatures in the 512 m M1 test region, simulated by Core without Godot.
/// Creatures (species <c>mossback</c> from the shipped data) have minds (rolled personality, memory store), choose goals with the
/// utility AI at 2 Hz (M1-08), wander at a goal-dependent speed, perceive neighbours (time-sliced), remember crowded spots as
/// dangerous (M1-06), react with emotions and an emote (M1-07) and are replaced by churn. Until the real creature
/// AI exists (Phase B) these systems stand in for it; each later system is added here so the gates keep covering it.
/// Used by the CI gates (0 allocations per tick, identical runs) and by the benchmark tool (timings).
/// </summary>
public sealed class B5Scenario
{
    /// <summary>Creatures alive at any time.</summary>
    public const int CreatureCount = 200;

    /// <summary>Warm-up: 30 s at 30 Hz (M0-07 A4).</summary>
    public const int WarmUpTicks = 900;

    /// <summary>Measured run: 5 min at 30 Hz (M0-07 A1).</summary>
    public const int MeasuredTicks = 9_000;

    private const float RegionSize = 512f;
    private const float Speed = 0.1f;
    private const int EntityCapacity = 256;
    private const int EventCapacity = 256;
    private const string SpeciesId = "mossback";

    private readonly EcsWorld _world;
    private readonly SpatialGrid _grid;
    private readonly SeededRandom _random;
    private readonly StableIdAllocator _ids = new();
    private readonly CreatureMindStore _minds;
    private readonly AppraisalModel _appraisal;
    private readonly SpeciesDefinition _species;
    private readonly SimulationClock _clock;
    private readonly SimulationScheduler _scheduler;

    /// <summary>Builds the scenario from a seed; <paramref name="metrics"/> optionally receives stage timings.</summary>
    public B5Scenario(ulong seed, IMetrics? metrics = null)
    {
        _random = new SeededRandom(seed);
        _world = CreateWorld();
        DataRegistry data = ShippedData.Current;
        _species = data.GetSpecies(SpeciesId);
        var rules = new MemoryRules(data.MemoryEventTypes, data.OpinionWeights, SimulationSettings.DefaultTicksPerDay);
        _minds = new CreatureMindStore(rules, EntityCapacity);
        var opinions = new OpinionModel(_minds, rules);
        _appraisal = new AppraisalModel(_minds, opinions, rules, AppraisalSettings.Default);
        _grid = new SpatialGrid(new SpatialGridSettings(0, 0, RegionSize, RegionSize, SpatialGridSettings.DefaultCellSize, EntityCapacity));

        Events = new EventBus();
        Events.Register<NeighbourCount>(EventCapacity);
        _clock = new SimulationClock(SimulationSettings.Default);
        _scheduler = new SimulationScheduler(_clock, Events, metrics);
        RegisterSystems(data, rules, new UtilityEvaluator(_minds, _appraisal, opinions, data.Goals, SimulationSettings.DefaultTicksPerDay));

        for (int i = 0; i < CreatureCount; i++)
        {
            Spawn();
        }
    }

    /// <summary>The scenario's event bus (its growth counter shows hidden allocations).</summary>
    public EventBus Events { get; }

    /// <summary>Creatures currently alive.</summary>
    public int LiveCreatures => _world.EntityCount;

    /// <summary>Simulates one tick.</summary>
    public void RunTick() => _scheduler.RunTick();

    /// <summary>Minds currently allocated.</summary>
    public int LiveMinds => _minds.Count;

    /// <summary>How many creatures currently pursue each goal id (for the gate test).</summary>
    public IReadOnlyDictionary<ushort, int> GoalCounts
    {
        get
        {
            var counts = new Dictionary<ushort, int>();
            foreach (CurrentGoal goal in _world.Store<CurrentGoal>().Components)
            {
                counts[goal.Goal] = counts.GetValueOrDefault(goal.Goal) + 1;
            }

            return counts;
        }
    }

    /// <summary>Creatures currently showing an emote.</summary>
    public int CreaturesShowingAnEmote
    {
        get
        {
            int count = 0;
            foreach (CurrentEmote emote in _world.Store<CurrentEmote>().Components)
            {
                count += emote.Value == Emote.None ? 0 : 1;
            }

            return count;
        }
    }

    /// <summary>Memory records currently stored by all creatures.</summary>
    public int StoredMemories => _minds.TotalRecords;

    /// <summary>Hash of all deterministic state: ids, positions, velocities, timers and minds, in dense order (TDD §2.3).</summary>
    public ulong ComputeStateHash()
    {
        var hash = default(StateHash);
        hash.Add(_clock.Current.Index);
        hash.Add<Entity>(_world.Store<Position>().Entities);
        hash.Add<StableId>(_world.Store<StableId>().Components);
        hash.Add<Position>(_world.Store<Position>().Components);
        hash.Add<Velocity>(_world.Store<Velocity>().Components);
        hash.Add<WanderTimer>(_world.Store<WanderTimer>().Components);
        hash.Add<CurrentEmote>(_world.Store<CurrentEmote>().Components);
        hash.Add<CurrentGoal>(_world.Store<CurrentGoal>().Components);
        _minds.AddTo(ref hash);
        return hash.Value;
    }

    private static EcsWorld CreateWorld()
    {
        var world = new EcsWorld(EntityCapacity);
        world.Register<StableId>();
        world.Register<Position>();
        world.Register<Velocity>();
        world.Register<WanderTimer>();
        world.Register<MindRef>();
        world.Register<CurrentEmote>();
        world.Register<CurrentGoal>();
        return world;
    }

    private void RegisterSystems(DataRegistry data, MemoryRules rules, UtilityEvaluator evaluator)
    {
        ushort placeDanger = rules.FindByKey("PlaceDanger") ?? throw new InvalidOperationException("Shipped data has no PlaceDanger memory event.");
        var goalIds = new B5Goals(GoalId(data, "Flee"), GoalId(data, "Eat"), GoalId(data, "Rest"), GoalId(data, "Socialize"));
        _scheduler.Register(new PerceptionSystem(_world, _grid, Events));
        _scheduler.Register(new DecisionSystem(_world, evaluator, _minds, evaluator.ForSpecies(_species), _random, goalIds));
        _scheduler.Register(new WanderSystem(_world, _random, Speed, goalIds));
        _scheduler.Register(new CrowdReactionSystem(_world, Events, _minds, _appraisal, placeDanger));
        _scheduler.Register(new MovementSystem(_world, _grid, RegionSize));
        _scheduler.Register(new ChurnSystem(this, _world));
    }

    private static ushort GoalId(DataRegistry data, string key) =>
        data.Goals.FirstOrDefault(goal => goal.Key == key)?.Id ?? throw new InvalidOperationException($"Shipped data has no goal '{key}'.");

    internal void Spawn()
    {
        Entity creature = _world.Create();
        var position = new Vector3((float)(_random.NextDouble() * RegionSize), 0, (float)(_random.NextDouble() * RegionSize));
        _world.Add(creature, _ids.Allocate());
        _world.Add(creature, new Position(position));
        _world.Add(creature, new Velocity(Vector3.Zero));
        _world.Add(creature, new WanderTimer(1));
        _world.Add(creature, new CurrentEmote(Emote.None));
        _world.Add(creature, new CurrentGoal(Decision.Idle));
        MindHandle mind = _minds.Allocate(Personality.Roll(_species, _random), _species.Needs);
        _world.Add(creature, new MindRef(mind));

        // Creatures enter the world with varied needs, so goals differ from the start.
        _minds.SetNeed(mind, Need.Hunger, _random.NextInt(0, ICreatureMindStore.MaxNeed + 1), _clock.Current.Index);
        _minds.SetNeed(mind, Need.Rest, _random.NextInt(0, ICreatureMindStore.MaxNeed + 1), _clock.Current.Index);
        _minds.SetNeed(mind, Need.Social, _random.NextInt(0, ICreatureMindStore.MaxNeed + 1), _clock.Current.Index);
        _grid.Insert(creature, position);
    }

    internal void Despawn(Entity creature)
    {
        _grid.Remove(creature);
        _minds.Release(_world.GetComponent<MindRef>(creature).Handle);
        _world.Destroy(creature);
    }
}
