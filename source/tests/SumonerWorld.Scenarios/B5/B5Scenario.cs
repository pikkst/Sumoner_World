using System.Numerics;
using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Events;
using SumonerWorld.Core.Randomness;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.Spatial;

namespace SumonerWorld.Scenarios.B5;

/// <summary>
/// Benchmark scenario B5 "Sim-Only" (M0-07 A1): 200 creatures in the 512 m M1 test region, simulated by Core without Godot.
/// Creatures wander, perceive neighbours (time-sliced), react to crowding and are replaced by churn. Until the real creature
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

    private readonly EcsWorld _world;
    private readonly SpatialGrid _grid;
    private readonly SeededRandom _random;
    private readonly StableIdAllocator _ids = new();
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
        _grid = new SpatialGrid(new SpatialGridSettings(0, 0, RegionSize, RegionSize, SpatialGridSettings.DefaultCellSize, EntityCapacity));

        Events = new EventBus();
        Events.Register<NeighbourCount>(EventCapacity);

        _clock = new SimulationClock(SimulationSettings.Default);
        _scheduler = new SimulationScheduler(_clock, Events, metrics);
        _scheduler.Register(new PerceptionSystem(_world, _grid, Events));
        _scheduler.Register(new WanderSystem(_world, _random, Speed));
        _scheduler.Register(new CrowdReactionSystem(_world, Events));
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

    /// <summary>Hash of all deterministic state: ids, positions, velocities, timers, in dense order (TDD §2.3).</summary>
    public ulong ComputeStateHash()
    {
        var hash = default(StateHash);
        hash.Add(_clock.Current.Index);
        hash.Add<Entity>(_world.Store<Position>().Entities);
        hash.Add<StableId>(_world.Store<StableId>().Components);
        hash.Add<Position>(_world.Store<Position>().Components);
        hash.Add<Velocity>(_world.Store<Velocity>().Components);
        hash.Add<WanderTimer>(_world.Store<WanderTimer>().Components);
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
        _grid.Insert(creature, position);
    }

    internal void Despawn(Entity creature)
    {
        _grid.Remove(creature);
        _world.Destroy(creature);
    }
}
