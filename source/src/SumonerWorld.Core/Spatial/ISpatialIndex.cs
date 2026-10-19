using System.Numerics;
using SumonerWorld.Core.Ecs;

namespace SumonerWorld.Core.Spatial;

/// <summary>
/// Finds entities near a point for perception, proximity, targeting, interest management and rumour sharing (R19,
/// AI Bible §9.3). Distances are measured on the ground plane (X/Z). Queries write into caller buffers and never allocate (R21).
/// Result order is deterministic: cells in row order (south to north, west to east), and within a cell the entity that entered
/// it most recently first (R11).
/// </summary>
public interface ISpatialIndex
{
    /// <summary>Number of entities in the index.</summary>
    int Count { get; }

    /// <summary>Adds an entity at a position. Throws if it is already present.</summary>
    void Insert(Entity entity, Vector3 position);

    /// <summary>Updates an entity's position. Throws if it is not present.</summary>
    void Move(Entity entity, Vector3 position);

    /// <summary>Removes an entity; returns false if it was not present.</summary>
    bool Remove(Entity entity);

    /// <summary>True if this exact entity (same generation) is in the index.</summary>
    bool Contains(Entity entity);

    /// <summary>The entity's last stored position; false if it is not in the index.</summary>
    bool TryGetPosition(Entity entity, out Vector3 position);

    /// <summary>Entities whose ground-plane distance to <paramref name="center"/> is at most <paramref name="radius"/>.</summary>
    SpatialQueryResult QueryRadius(Vector3 center, float radius, Span<Entity> results);

    /// <summary>
    /// Entities within <paramref name="range"/> of <paramref name="origin"/> and inside a view cone around
    /// <paramref name="forward"/> (ground plane). <paramref name="halfAngleRadians"/> is the angle from the centre line to the edge.
    /// </summary>
    SpatialQueryResult QueryCone(Vector3 origin, Vector3 forward, float range, float halfAngleRadians, Span<Entity> results);
}
