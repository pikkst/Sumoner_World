using BenchmarkDotNet.Attributes;
using SumonerWorld.Core.Ecs;

namespace SumonerWorld.Benchmarks.EcsSpike;

/// <summary>
/// The production <see cref="EcsWorld"/> (M1-02b) on the same operations as <see cref="EcsSpikeBenchmarks"/>, to confirm it
/// keeps the spike's numbers (ADR-002).
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class CoreEcsBenchmarks
{
    private const int ChurnDivisor = 10;
    private const int SpawnDivisor = 20;

    private EcsWorld _world = null!;
    private Entity[] _entities = [];
    private ComponentStore<Position> _positions = null!;
    private ComponentStore<Velocity> _velocities = null!;
    private ComponentStore<AiTier> _tiers = null!;
    private int _tick;

    /// <summary>Entity count: the M1 gate (200 creatures) and a stress case.</summary>
    [Params(200, 10_000)]
    public int Count { get; set; }

    /// <summary>Builds the world.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _world = new EcsWorld(Count * 2);
        _world.Register<Position>();
        _world.Register<Velocity>();
        _world.Register<AiTier>();
        _world.Register<ActiveTag>();
        _positions = _world.Store<Position>();
        _velocities = _world.Store<Velocity>();
        _tiers = _world.Store<AiTier>();
        _entities = new Entity[Count];

        for (int i = 0; i < Count; i++)
        {
            _entities[i] = Spawn(i, (byte)(i % 3));
        }
    }

    /// <summary>Movement system.</summary>
    [Benchmark]
    public void Core_Movement()
    {
        foreach (Entity entity in _world.View<Position, Velocity>())
        {
            ref Position position = ref _positions.Get(entity);
            ref Velocity velocity = ref _velocities.Get(entity);
            position = new Position(position.X + velocity.X, position.Y + velocity.Y, position.Z + velocity.Z);
        }
    }

    /// <summary>Movement system using the dense fast path: iterate one store's arrays, look up only the other component.</summary>
    [Benchmark]
    public void Core_MovementDense()
    {
        ReadOnlySpan<Entity> entities = _velocities.Entities;
        Span<Velocity> velocities = _velocities.Components;
        for (int i = 0; i < entities.Length; i++)
        {
            ref Position position = ref _positions.Get(entities[i]);
            Velocity velocity = velocities[i];
            position = new Position(position.X + velocity.X, position.Y + velocity.Y, position.Z + velocity.Z);
        }
    }

    /// <summary>Three-component query with a filter.</summary>
    [Benchmark]
    public int Core_TierQuery()
    {
        int active = 0;
        foreach (Entity entity in _world.View<Position, Velocity, AiTier>())
        {
            if (_tiers.Get(entity).Tier == 0)
            {
                active++;
            }
        }

        return active;
    }

    /// <summary>10% of entities toggle the active tag.</summary>
    [Benchmark]
    public void Core_TierChurn()
    {
        int offset = _tick++ % ChurnDivisor;
        for (int i = offset; i < _entities.Length; i += ChurnDivisor)
        {
            Entity entity = _entities[i];
            if (!_world.Remove<ActiveTag>(entity))
            {
                _world.Add(entity, default(ActiveTag));
            }
        }
    }

    /// <summary>5% of entities destroyed and re-created.</summary>
    [Benchmark]
    public void Core_Spawn()
    {
        int offset = _tick++ % SpawnDivisor;
        for (int i = offset; i < _entities.Length; i += SpawnDivisor)
        {
            _world.Destroy(_entities[i]);
            _entities[i] = Spawn(i, 1);
        }
    }

    private Entity Spawn(int i, byte tier)
    {
        Entity entity = _world.Create();
        _world.Add(entity, new Position(i, 0, i));
        _world.Add(entity, new Velocity(0.1f, 0, 0.1f));
        _world.Add(entity, new AiTier(tier));
        return entity;
    }
}
