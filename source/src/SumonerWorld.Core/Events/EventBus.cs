using System.Runtime.CompilerServices;

namespace SumonerWorld.Core.Events;

/// <summary>
/// Pre-allocated <see cref="IEventBus"/>. Event types are registered up front with a capacity; publishing never allocates
/// unless a buffer outgrows its capacity, which is counted in <see cref="CapacityGrowths"/> so the benchmark can flag it (R21).
/// </summary>
public sealed class EventBus : IEventBus
{
    private readonly Dictionary<Type, EventBuffer> _buffersByType = [];
    private readonly List<EventBuffer> _buffers = [];

    /// <summary>How many times any buffer had to grow beyond its registered capacity.</summary>
    public int CapacityGrowths { get; private set; }

    /// <summary>Registers an event type with room for <paramref name="initialCapacity"/> events per tick.</summary>
    public void Register<TEvent>(int initialCapacity)
        where TEvent : struct
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialCapacity);

        if (_buffersByType.ContainsKey(typeof(TEvent)))
        {
            throw new InvalidOperationException($"Event type {typeof(TEvent).Name} is already registered.");
        }

        var buffer = new EventBuffer<TEvent>(initialCapacity, this);
        _buffersByType.Add(typeof(TEvent), buffer);
        _buffers.Add(buffer);
    }

    /// <inheritdoc />
    public void Publish<TEvent>(in TEvent gameEvent)
        where TEvent : struct => GetBuffer<TEvent>().Add(in gameEvent);

    /// <inheritdoc />
    public ReadOnlySpan<TEvent> Read<TEvent>()
        where TEvent : struct => GetBuffer<TEvent>().AsSpan();

    /// <summary>Clears every buffer. Called by the scheduler at the end of each tick.</summary>
    public void EndTick()
    {
        for (int i = 0; i < _buffers.Count; i++)
        {
            _buffers[i].Clear();
        }
    }

    private EventBuffer<TEvent> GetBuffer<TEvent>()
        where TEvent : struct
    {
        if (_buffersByType.TryGetValue(typeof(TEvent), out EventBuffer? buffer))
        {
            return (EventBuffer<TEvent>)buffer;
        }

        throw new InvalidOperationException(
            $"Event type {typeof(TEvent).Name} is not registered. Register it with EventBus.Register before the first tick.");
    }

    private void OnGrowth() => CapacityGrowths++;

    private abstract class EventBuffer
    {
        public abstract void Clear();
    }

    private sealed class EventBuffer<TEvent> : EventBuffer
        where TEvent : struct
    {
        private readonly EventBus _owner;
        private TEvent[] _items;
        private int _count;

        public EventBuffer(int capacity, EventBus owner)
        {
            _items = new TEvent[capacity];
            _owner = owner;
        }

        public void Add(in TEvent item)
        {
            if (_count == _items.Length)
            {
                Array.Resize(ref _items, _items.Length * 2);
                _owner.OnGrowth();
            }

            _items[_count++] = item;
        }

        public ReadOnlySpan<TEvent> AsSpan() => new(_items, 0, _count);

        public override void Clear()
        {
            if (RuntimeHelpers.IsReferenceOrContainsReferences<TEvent>())
            {
                Array.Clear(_items, 0, _count);
            }

            _count = 0;
        }
    }
}
