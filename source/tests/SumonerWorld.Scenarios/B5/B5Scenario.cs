using System.Numerics;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Cognition.Opinions;
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
/// Creatures have minds (rolled personality, memory store), wander, perceive neighbours (time-sliced), remember crowded spots as
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
    private const ushort PlaceDanger = 16;

    // Stand-in species and memory rules (values from the shipped data) so the scenario does not read files.
    private static readonly SpeciesDefinition Species = new()
    {
        Id = "b5_grazer",
        NameKey = "b5",
        Role = CreatureRole.Grazer,
        BodyType = BodyType.Quadruped,
        Group = GroupType.Herd,
        Boldness = new TraitRange { Mean = -30, Spread = 25 },
        Loyalty = new TraitRange { Mean = 40, Spread = 30 },
        Curiosity = new TraitRange { Mean = 10, Spread = 30 },
        Temper = new TraitRange { Mean = -40, Spread = 20 },
        Needs = new NeedRates { Hunger = 60, Rest = 40, Safety = 50, Social = 70, Territory = 10 },
        PactThreshold = 450,
    };

    private static readonly MemoryEventTypeDefinition PlaceDangerDefinition = new()
    {
        Id = PlaceDanger,
        Key = "PlaceDanger",
        Subjects = [SubjectKind.Place],
        Valence = -40,
        BaseSalience = 300,
        HalfLifeDays = 14,
        IsCore = false,
    };

    private static readonly OpinionWeightsDefinition PlaceDangerWeights = new() { EventType = PlaceDanger, Trust = 0, Fear = 60, Affection = 0, Respect = 0 };

    private readonly EcsWorld _world;
    private readonly SpatialGrid _grid;
    private readonly SeededRandom _random;
    private readonly StableIdAllocator _ids = new();
    private readonly CreatureMindStore _minds;
    private readonly AppraisalModel _appraisal;
    private readonly SimulationClock _clock;
    private readonly SimulationScheduler _scheduler;

    /// <summary>Builds the scenario from a seed; <paramref name="metrics"/> optionally receives stage timings.</summary>
    public B5Scenario(ulong seed, IMetrics? metrics = null)
    {
        _random = new SeededRandom(seed);
        _world = new EcsWorld(EntityCapacity);
        _world.Register<StableId>();
        _world.Register<Position>();
        _world.Register<Velocity>();
        _world.Register<WanderTimer>();
        _world.Register<MindRef>();
        _world.Register<CurrentEmote>();
        var rules = new MemoryRules([PlaceDangerDefinition], [PlaceDangerWeights], SimulationSettings.DefaultTicksPerDay);
        _minds = new CreatureMindStore(rules, EntityCapacity);
        _appraisal = new AppraisalModel(_minds, new OpinionModel(_minds, rules), rules, AppraisalSettings.Default);
        _grid = new SpatialGrid(new SpatialGridSettings(0, 0, RegionSize, RegionSize, SpatialGridSettings.DefaultCellSize, EntityCapacity));

        Events = new EventBus();
        Events.Register<NeighbourCount>(EventCapacity);

        _clock = new SimulationClock(SimulationSettings.Default);
        _scheduler = new SimulationScheduler(_clock, Events, metrics);
        _scheduler.Register(new PerceptionSystem(_world, _grid, Events));
        _scheduler.Register(new WanderSystem(_world, _random, Speed));
        _scheduler.Register(new CrowdReactionSystem(_world, Events, _minds, _appraisal, PlaceDanger));
        _scheduler.Register(new MovementSystem(_world, _grid, RegionSize));
        _scheduler.Register(new ChurnSystem(this, _world));

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
        _minds.AddTo(ref hash);
        return hash.Value;
    }

    internal void Spawn()
    {
        Entity creature = _world.Create();
        var position = new Vector3((float)(_random.NextDouble() * RegionSize), 0, (float)(_random.NextDouble() * RegionSize));
        _world.Add(creature, _ids.Allocate());
        _world.Add(creature, new Position(position));
        _world.Add(creature, new Velocity(Vector3.Zero));
        _world.Add(creature, new WanderTimer(1));
        _world.Add(creature, new CurrentEmote(Emote.None));
        _world.Add(creature, new MindRef(_minds.Allocate(Personality.Roll(Species, _random), Species.Needs)));
        _grid.Insert(creature, position);
    }

    internal void Despawn(Entity creature)
    {
        _grid.Remove(creature);
        _minds.Release(_world.GetComponent<MindRef>(creature).Handle);
        _world.Destroy(creature);
    }
}
