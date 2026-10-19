namespace SumonerWorld.Core.Ecs;

/// <summary>
/// In-house sparse-set <see cref="IEcsWorld"/> (ADR-002). Capacity and component types are fixed at setup, so nothing
/// allocates during ticks (R21). Every operation's effect on order is defined, so two worlds given the same operations
/// iterate identically (R11).
/// </summary>
public sealed class EcsWorld : IEcsWorld
{
    private readonly int[] _generations;
    private readonly bool[] _alive;
    private readonly int[] _freeSlots;
    private readonly List<ComponentStore> _stores = [];
    private ComponentStore?[] _storesByTypeIndex = [];
    private int _freeCount;
    private int _nextUnusedSlot;

    /// <summary>Creates a world for at most <paramref name="capacity"/> live entities (from server config).</summary>
    public EcsWorld(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        Capacity = capacity;
        _generations = new int[capacity];
        _alive = new bool[capacity];
        _freeSlots = new int[capacity];
    }

    /// <inheritdoc />
    public int Capacity { get; }

    /// <inheritdoc />
    public int EntityCount { get; private set; }

    /// <summary>Registers a component type. Call during setup, before the first tick.</summary>
    public void Register<T>()
        where T : struct
    {
        int typeIndex = ComponentTypeIndex<T>.Value;
        if (typeIndex < _storesByTypeIndex.Length && _storesByTypeIndex[typeIndex] is not null)
        {
            throw new InvalidOperationException($"Component type {typeof(T).Name} is already registered.");
        }

        if (typeIndex >= _storesByTypeIndex.Length)
        {
            Array.Resize(ref _storesByTypeIndex, Math.Max(typeIndex + 1, ComponentTypeIndex.Count));
        }

        var store = new ComponentStore<T>(Capacity);
        _storesByTypeIndex[typeIndex] = store;
        _stores.Add(store);
    }

    /// <inheritdoc />
    public Entity Create()
    {
        int index;
        if (_freeCount > 0)
        {
            index = _freeSlots[--_freeCount];
        }
        else if (_nextUnusedSlot < Capacity)
        {
            index = _nextUnusedSlot++;
        }
        else
        {
            throw new InvalidOperationException(
                $"Entity capacity {Capacity} reached. Raise the world's entity capacity in the server config.");
        }

        _alive[index] = true;
        EntityCount++;
        return new Entity(index, _generations[index]);
    }

    /// <inheritdoc />
    public void Destroy(Entity entity)
    {
        EnsureAlive(entity);

        for (int i = 0; i < _stores.Count; i++)
        {
            _stores[i].Remove(entity);
        }

        _alive[entity.Index] = false;
        _generations[entity.Index]++;
        _freeSlots[_freeCount++] = entity.Index;
        EntityCount--;
    }

    /// <inheritdoc />
    public bool IsAlive(Entity entity) =>
        (uint)entity.Index < (uint)Capacity
        && _alive[entity.Index]
        && _generations[entity.Index] == entity.Generation;

    /// <inheritdoc />
    public void Add<T>(Entity entity, in T component)
        where T : struct
    {
        EnsureAlive(entity);
        Store<T>().Add(entity, in component);
    }

    /// <inheritdoc />
    public bool Remove<T>(Entity entity)
        where T : struct
    {
        EnsureAlive(entity);
        return Store<T>().Remove(entity);
    }

    /// <inheritdoc />
    public bool Has<T>(Entity entity)
        where T : struct => IsAlive(entity) && Store<T>().Has(entity);

    /// <inheritdoc />
    public ref T GetComponent<T>(Entity entity)
        where T : struct
    {
        EnsureAlive(entity);
        return ref Store<T>().Get(entity);
    }

    /// <inheritdoc />
    public ComponentStore<T> Store<T>()
        where T : struct
    {
        int typeIndex = ComponentTypeIndex<T>.Value;
        if (typeIndex < _storesByTypeIndex.Length && _storesByTypeIndex[typeIndex] is ComponentStore<T> store)
        {
            return store;
        }

        throw new InvalidOperationException(
            $"Component type {typeof(T).Name} is not registered. Register it with EcsWorld.Register before the first tick.");
    }

    /// <inheritdoc />
    public EntityView View<T1, T2>()
        where T1 : struct
        where T2 : struct => new(Store<T1>(), Store<T2>(), null, null);

    /// <inheritdoc />
    public EntityView View<T1, T2, T3>()
        where T1 : struct
        where T2 : struct
        where T3 : struct => new(Store<T1>(), Store<T2>(), Store<T3>(), null);

    /// <inheritdoc />
    public EntityView View<T1, T2, T3, T4>()
        where T1 : struct
        where T2 : struct
        where T3 : struct
        where T4 : struct => new(Store<T1>(), Store<T2>(), Store<T3>(), Store<T4>());

    private void EnsureAlive(Entity entity)
    {
        if (!IsAlive(entity))
        {
            throw new InvalidOperationException($"{entity} is not alive (destroyed, or never created in this world).");
        }
    }
}
