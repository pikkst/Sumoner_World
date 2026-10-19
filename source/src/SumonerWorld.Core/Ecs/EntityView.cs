namespace SumonerWorld.Core.Ecs;

/// <summary>
/// Entities that have every component in a set (ADR-002). Iterates the smallest store and checks the others, so the cost
/// follows the rarest component. Use with <c>foreach</c>; it does not allocate.
/// </summary>
public readonly ref struct EntityView
{
    private readonly ComponentStore _driver;
    private readonly ComponentStore? _filterA;
    private readonly ComponentStore? _filterB;
    private readonly ComponentStore? _filterC;

    internal EntityView(ComponentStore first, ComponentStore second, ComponentStore? third, ComponentStore? fourth)
    {
        ComponentStore driver = first;
        if (second.Count < driver.Count)
        {
            driver = second;
        }

        if (third is not null && third.Count < driver.Count)
        {
            driver = third;
        }

        if (fourth is not null && fourth.Count < driver.Count)
        {
            driver = fourth;
        }

        // Every store except the driver becomes a filter, in the original order.
        _driver = driver;
        if (ReferenceEquals(second, driver))
        {
            _filterA = first;
            _filterB = third;
            _filterC = fourth;
        }
        else if (third is not null && ReferenceEquals(third, driver))
        {
            _filterA = first;
            _filterB = second;
            _filterC = fourth;
        }
        else if (fourth is not null && ReferenceEquals(fourth, driver))
        {
            _filterA = first;
            _filterB = second;
            _filterC = third;
        }
        else
        {
            _filterA = second;
            _filterB = third;
            _filterC = fourth;
        }
    }

    /// <summary>Returns the enumerator used by <c>foreach</c>.</summary>
    public Enumerator GetEnumerator() => new(_driver, _filterA, _filterB, _filterC);

    /// <summary>Walks the matching entities in the driver store's dense order.</summary>
    public ref struct Enumerator
    {
        private readonly ComponentStore _driver;
        private readonly ComponentStore? _filterA;
        private readonly ComponentStore? _filterB;
        private readonly ComponentStore? _filterC;
        private readonly int _version;
        private readonly ReadOnlySpan<Entity> _entities;
        private int _index;

        internal Enumerator(ComponentStore driver, ComponentStore? filterA, ComponentStore? filterB, ComponentStore? filterC)
        {
            _driver = driver;
            _filterA = filterA;
            _filterB = filterB;
            _filterC = filterC;
            _version = driver.Version;
            _entities = driver.Entities;
            _index = -1;
        }

        /// <summary>The current matching entity.</summary>
        public readonly Entity Current => _entities[_index];

        /// <summary>Advances to the next matching entity.</summary>
        public bool MoveNext()
        {
            if (_driver.Version != _version)
            {
                throw new InvalidOperationException(
                    "Components were added or removed while iterating a view of them. Collect the changes and apply them after the loop.");
            }

            while (++_index < _entities.Length)
            {
                Entity entity = _entities[_index];
                if ((_filterA is null || _filterA.Has(entity))
                    && (_filterB is null || _filterB.Has(entity))
                    && (_filterC is null || _filterC.Has(entity)))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
