namespace SumonerWorld.Benchmarks.EcsSpike;

/// <summary>Entity handle: slot index plus generation, so a stale handle never reaches a reused slot.</summary>
internal readonly record struct SpikeEntity(int Index, int Generation);

/// <summary>
/// Minimal in-house sparse-set ECS used only to compare against Arch (M1-02).
/// Each component type has a pool with a dense array (cache-friendly iteration) and a sparse index (O(1) lookup).
/// </summary>
internal sealed class SparseSetWorld
{
    private readonly int[] _generations;
    private readonly Stack<int> _freeSlots;
    private readonly Dictionary<Type, IPool> _pools = [];
    private int _nextSlot;

    public SparseSetWorld(int capacity)
    {
        _generations = new int[capacity];
        _freeSlots = new Stack<int>(capacity);
    }

    public SpikeEntity Create()
    {
        int index = _freeSlots.Count > 0 ? _freeSlots.Pop() : _nextSlot++;
        return new SpikeEntity(index, _generations[index]);
    }

    public void Destroy(SpikeEntity entity)
    {
        foreach (IPool pool in _pools.Values)
        {
            pool.Remove(entity.Index);
        }

        _generations[entity.Index]++;
        _freeSlots.Push(entity.Index);
    }

    public ComponentPool<T> Pool<T>()
        where T : struct
    {
        if (!_pools.TryGetValue(typeof(T), out IPool? pool))
        {
            pool = new ComponentPool<T>(_generations.Length);
            _pools.Add(typeof(T), pool);
        }

        return (ComponentPool<T>)pool;
    }

    internal interface IPool
    {
        void Remove(int entityIndex);
    }
}

/// <summary>Dense storage for one component type.</summary>
internal sealed class ComponentPool<T> : SparseSetWorld.IPool
    where T : struct
{
    private const int Absent = -1;

    private readonly int[] _sparse;
    private readonly int[] _denseEntities;
    private readonly T[] _dense;

    public ComponentPool(int capacity)
    {
        _sparse = new int[capacity];
        Array.Fill(_sparse, Absent);
        _denseEntities = new int[capacity];
        _dense = new T[capacity];
    }

    public int Count { get; private set; }

    public ReadOnlySpan<int> Entities => new(_denseEntities, 0, Count);

    public Span<T> Components => new(_dense, 0, Count);

    public bool Has(int entityIndex) => _sparse[entityIndex] != Absent;

    public ref T Get(int entityIndex) => ref _dense[_sparse[entityIndex]];

    public void Add(int entityIndex, in T component)
    {
        _sparse[entityIndex] = Count;
        _denseEntities[Count] = entityIndex;
        _dense[Count] = component;
        Count++;
    }

    public void Remove(int entityIndex)
    {
        int slot = _sparse[entityIndex];
        if (slot == Absent)
        {
            return;
        }

        int last = Count - 1;
        _dense[slot] = _dense[last];
        _denseEntities[slot] = _denseEntities[last];
        _sparse[_denseEntities[slot]] = slot;
        _sparse[entityIndex] = Absent;
        Count--;
    }
}
