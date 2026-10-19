using Arch.Core;
using BenchmarkDotNet.Attributes;

namespace SumonerWorld.Benchmarks.EcsSpike;

/// <summary>Position in metres.</summary>
public record struct Position(float X, float Y, float Z);

/// <summary>Velocity in metres per tick.</summary>
public record struct Velocity(float X, float Y, float Z);

/// <summary>AI LOD tier (AI Bible §9.1).</summary>
public record struct AiTier(byte Tier);

/// <summary>Tag for creatures in the active tier.</summary>
public record struct ActiveTag;

/// <summary>
/// M1-02 spike: one simulated tick of the work an ECS does most, for Arch 2.1.0 vs. the in-house sparse set.
/// Movement = iterate Position+Velocity. TierQuery = iterate entities with three components and filter.
/// TierChurn = 10% of entities switch AI tier (tag add/remove, a structural change). Spawn = 5% destroyed and re-created.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class EcsSpikeBenchmarks
{
    private const int ChurnDivisor = 10;
    private const int SpawnDivisor = 20;

    private World _arch = null!;
    private Entity[] _archEntities = [];
    private QueryDescription _archMove;
    private QueryDescription _archTier;

    private SparseSetWorld _sparse = null!;
    private SpikeEntity[] _sparseEntities = [];

    private int _tick;

    /// <summary>Entity count: the M1 gate (200 creatures) and a stress case.</summary>
    [Params(200, 10_000)]
    public int Count { get; set; }

    /// <summary>Builds both worlds with identical content.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _arch = World.Create();
        _archEntities = new Entity[Count];
        _archMove = new QueryDescription().WithAll<Position, Velocity>();
        _archTier = new QueryDescription().WithAll<Position, Velocity, AiTier>();

        _sparse = new SparseSetWorld(Count * 2);
        _sparseEntities = new SpikeEntity[Count];

        for (int i = 0; i < Count; i++)
        {
            var position = new Position(i, 0, i);
            var velocity = new Velocity(0.1f, 0, 0.1f);
            var tier = new AiTier((byte)(i % 3));

            _archEntities[i] = _arch.Create(position, velocity, tier);

            SpikeEntity entity = _sparse.Create();
            _sparse.Pool<Position>().Add(entity.Index, position);
            _sparse.Pool<Velocity>().Add(entity.Index, velocity);
            _sparse.Pool<AiTier>().Add(entity.Index, tier);
            _sparseEntities[i] = entity;
        }
    }

    /// <summary>Releases the Arch world.</summary>
    [GlobalCleanup]
    public void Cleanup() => World.Destroy(_arch);

    /// <summary>Arch: movement system.</summary>
    [Benchmark]
    public void Arch_Movement()
    {
        var forEach = new MoveForEach();
        _arch.InlineQuery<MoveForEach, Position, Velocity>(in _archMove, ref forEach);
    }

    /// <summary>In-house: movement system.</summary>
    [Benchmark]
    public void Sparse_Movement()
    {
        ComponentPool<Position> positions = _sparse.Pool<Position>();
        ComponentPool<Velocity> velocities = _sparse.Pool<Velocity>();
        ReadOnlySpan<int> entities = velocities.Entities;
        Span<Velocity> velocitySpan = velocities.Components;

        for (int i = 0; i < entities.Length; i++)
        {
            ref Position position = ref positions.Get(entities[i]);
            ref Velocity velocity = ref velocitySpan[i];
            position = new Position(position.X + velocity.X, position.Y + velocity.Y, position.Z + velocity.Z);
        }
    }

    /// <summary>Arch: three-component query with a filter.</summary>
    [Benchmark]
    public int Arch_TierQuery()
    {
        var forEach = new CountActiveForEach();
        _arch.InlineQuery<CountActiveForEach, Position, Velocity, AiTier>(in _archTier, ref forEach);
        return forEach.Active;
    }

    /// <summary>In-house: three-component query with a filter.</summary>
    [Benchmark]
    public int Sparse_TierQuery()
    {
        ComponentPool<Position> positions = _sparse.Pool<Position>();
        ComponentPool<Velocity> velocities = _sparse.Pool<Velocity>();
        ComponentPool<AiTier> tiers = _sparse.Pool<AiTier>();
        ReadOnlySpan<int> entities = tiers.Entities;
        Span<AiTier> tierSpan = tiers.Components;
        int active = 0;

        for (int i = 0; i < entities.Length; i++)
        {
            int entity = entities[i];
            if (tierSpan[i].Tier == 0 && positions.Has(entity) && velocities.Has(entity))
            {
                active++;
            }
        }

        return active;
    }

    /// <summary>Arch: 10% of entities toggle the active tag (archetype move).</summary>
    [Benchmark]
    public void Arch_TierChurn()
    {
        int offset = _tick++ % ChurnDivisor;
        for (int i = offset; i < _archEntities.Length; i += ChurnDivisor)
        {
            Entity entity = _archEntities[i];
            if (_arch.Has<ActiveTag>(entity))
            {
                _arch.Remove<ActiveTag>(entity);
            }
            else
            {
                _arch.Add(entity, default(ActiveTag));
            }
        }
    }

    /// <summary>In-house: 10% of entities toggle the active tag.</summary>
    [Benchmark]
    public void Sparse_TierChurn()
    {
        ComponentPool<ActiveTag> tags = _sparse.Pool<ActiveTag>();
        int offset = _tick++ % ChurnDivisor;
        for (int i = offset; i < _sparseEntities.Length; i += ChurnDivisor)
        {
            int entity = _sparseEntities[i].Index;
            if (tags.Has(entity))
            {
                tags.Remove(entity);
            }
            else
            {
                tags.Add(entity, default);
            }
        }
    }

    /// <summary>Arch: 5% of entities destroyed and re-created (spawning, deaths).</summary>
    [Benchmark]
    public void Arch_Spawn()
    {
        int offset = _tick++ % SpawnDivisor;
        for (int i = offset; i < _archEntities.Length; i += SpawnDivisor)
        {
            _arch.Destroy(_archEntities[i]);
            _archEntities[i] = _arch.Create(new Position(i, 0, i), new Velocity(0.1f, 0, 0.1f), new AiTier(1));
        }
    }

    /// <summary>In-house: 5% of entities destroyed and re-created.</summary>
    [Benchmark]
    public void Sparse_Spawn()
    {
        int offset = _tick++ % SpawnDivisor;
        for (int i = offset; i < _sparseEntities.Length; i += SpawnDivisor)
        {
            _sparse.Destroy(_sparseEntities[i]);
            SpikeEntity entity = _sparse.Create();
            _sparse.Pool<Position>().Add(entity.Index, new Position(i, 0, i));
            _sparse.Pool<Velocity>().Add(entity.Index, new Velocity(0.1f, 0, 0.1f));
            _sparse.Pool<AiTier>().Add(entity.Index, new AiTier(1));
            _sparseEntities[i] = entity;
        }
    }

    private struct MoveForEach : IForEach<Position, Velocity>
    {
        public readonly void Update(ref Position position, ref Velocity velocity) =>
            position = new Position(position.X + velocity.X, position.Y + velocity.Y, position.Z + velocity.Z);
    }

    private struct CountActiveForEach : IForEach<Position, Velocity, AiTier>
    {
        public int Active;

        public void Update(ref Position position, ref Velocity velocity, ref AiTier tier)
        {
            if (tier.Tier == 0)
            {
                Active++;
            }
        }
    }
}
