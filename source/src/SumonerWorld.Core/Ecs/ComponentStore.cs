using System.Runtime.CompilerServices;

namespace SumonerWorld.Core.Ecs;

/// <summary>
/// Type-independent part of a sparse-set component store (ADR-002): which entities have the component, in dense order.
/// Membership checks are non-virtual so views stay fast.
/// </summary>
public abstract class ComponentStore
{
    private protected const int Absent = -1;

    private protected readonly int[] _sparse;
    private protected readonly Entity[] _denseEntities;

    private protected ComponentStore(int capacity)
    {
        _sparse = new int[capacity];
        Array.Fill(_sparse, Absent);
        _denseEntities = new Entity[capacity];
    }

    /// <summary>Number of entities that have this component.</summary>
    public int Count { get; private protected set; }

    /// <summary>Incremented on every add or remove; views use it to detect structural changes during iteration.</summary>
    public int Version { get; private protected set; }

    /// <summary>Entities that have this component, in dense order.</summary>
    public ReadOnlySpan<Entity> Entities => new(_denseEntities, 0, Count);

    /// <summary>True if <paramref name="entity"/> (this exact generation) has the component.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Has(Entity entity)
    {
        int slot = _sparse[entity.Index];
        return slot != Absent && _denseEntities[slot].Generation == entity.Generation;
    }

    /// <summary>Removes the component if present; returns whether it was present.</summary>
    internal abstract bool Remove(Entity entity);
}

/// <summary>
/// Sparse-set storage for one component type (ADR-002): a dense array for cache-friendly iteration and a sparse index for O(1)
/// lookup. Removal swaps the last element into the gap, so the dense order is fully determined by the sequence of operations.
/// </summary>
/// <typeparam name="T">Component type: a plain struct.</typeparam>
public sealed class ComponentStore<T> : ComponentStore
    where T : struct
{
    private readonly T[] _dense;

    internal ComponentStore(int capacity)
        : base(capacity)
    {
        _dense = new T[capacity];
    }

    /// <summary>Components in the same order as <see cref="ComponentStore.Entities"/>. Values may be changed in place.</summary>
    public Span<T> Components => new(_dense, 0, Count);

    /// <summary>Returns a reference to the entity's component. Throws if the entity does not have one.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T Get(Entity entity)
    {
        int slot = _sparse[entity.Index];
        if (slot == Absent || _denseEntities[slot].Generation != entity.Generation)
        {
            ThrowMissing(entity);
        }

        return ref _dense[slot];
    }

    internal void Add(Entity entity, in T component)
    {
        if (_sparse[entity.Index] != Absent)
        {
            throw new InvalidOperationException($"{entity} already has a {typeof(T).Name} component.");
        }

        _sparse[entity.Index] = Count;
        _denseEntities[Count] = entity;
        _dense[Count] = component;
        Count++;
        Version++;
    }

    /// <inheritdoc />
    internal override bool Remove(Entity entity)
    {
        if (!Has(entity))
        {
            return false;
        }

        int slot = _sparse[entity.Index];
        int last = Count - 1;
        _dense[slot] = _dense[last];
        _denseEntities[slot] = _denseEntities[last];
        _sparse[_denseEntities[slot].Index] = slot;
        _sparse[entity.Index] = Absent;
        _dense[last] = default;
        Count--;
        Version++;
        return true;
    }

    private static void ThrowMissing(Entity entity) =>
        throw new InvalidOperationException($"{entity} has no {typeof(T).Name} component.");
}
