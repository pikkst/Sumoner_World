namespace SumonerWorld.Core.Ecs;

/// <summary>
/// Entities and their components (ADR-002, TDD §2.2). Fixed capacity, no allocations after setup, deterministic order.
/// Structural changes (create, destroy, add, remove) are not allowed while iterating a view of the affected component types.
/// </summary>
public interface IEcsWorld
{
    /// <summary>Most entities that can exist at once.</summary>
    int Capacity { get; }

    /// <summary>Entities currently alive.</summary>
    int EntityCount { get; }

    /// <summary>Creates an entity with no components. Freed slots are reused last-in, first-out.</summary>
    Entity Create();

    /// <summary>Destroys a live entity and removes all its components.</summary>
    void Destroy(Entity entity);

    /// <summary>True if <paramref name="entity"/> is this exact live entity (not a destroyed one in a reused slot).</summary>
    bool IsAlive(Entity entity);

    /// <summary>Adds a component to a live entity. Throws if it already has one of this type.</summary>
    void Add<T>(Entity entity, in T component)
        where T : struct;

    /// <summary>Removes a component; returns false if the entity did not have it.</summary>
    bool Remove<T>(Entity entity)
        where T : struct;

    /// <summary>True if the entity has a component of this type.</summary>
    bool Has<T>(Entity entity)
        where T : struct;

    /// <summary>Reference to the entity's component. Throws if it is missing.</summary>
    ref T GetComponent<T>(Entity entity)
        where T : struct;

    /// <summary>The store for a registered component type, for tight loops. Look it up once, outside the tick.</summary>
    ComponentStore<T> Store<T>()
        where T : struct;

    /// <summary>Entities that have both component types.</summary>
    EntityView View<T1, T2>()
        where T1 : struct
        where T2 : struct;

    /// <summary>Entities that have all three component types.</summary>
    EntityView View<T1, T2, T3>()
        where T1 : struct
        where T2 : struct
        where T3 : struct;

    /// <summary>Entities that have all four component types.</summary>
    EntityView View<T1, T2, T3, T4>()
        where T1 : struct
        where T2 : struct
        where T3 : struct
        where T4 : struct;
}
