using System.Numerics;
using SumonerWorld.Core.Ecs;

namespace SumonerWorld.Core.Spatial;

/// <summary>
/// Uniform grid over the bounded world (<see cref="ISpatialIndex"/>). Each cell keeps an intrusive doubly linked list of
/// entity indices in fixed arrays, so insert, move and remove are O(1) and nothing allocates after construction (R21).
/// Positions outside the bounds are kept as they are and filed in the nearest edge cell, so queries stay correct.
/// </summary>
public sealed class SpatialGrid : ISpatialIndex
{
    private const int None = -1;

    private readonly SpatialGridSettings _settings;
    private readonly float _inverseCellSize;
    private readonly int[] _cellHeads;
    private readonly int[] _next;
    private readonly int[] _previous;
    private readonly int[] _cellOf;
    private readonly Entity[] _entities;
    private readonly Vector3[] _positions;

    /// <summary>Creates an empty grid.</summary>
    public SpatialGrid(SpatialGridSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;
        _inverseCellSize = 1f / settings.CellSize;
        _cellHeads = new int[settings.CellsX * settings.CellsZ];
        Array.Fill(_cellHeads, None);

        int capacity = settings.EntityCapacity;
        _next = new int[capacity];
        _previous = new int[capacity];
        _cellOf = new int[capacity];
        Array.Fill(_cellOf, None);
        _entities = new Entity[capacity];
        _positions = new Vector3[capacity];
    }

    /// <inheritdoc />
    public int Count { get; private set; }

    /// <inheritdoc />
    public void Insert(Entity entity, Vector3 position)
    {
        ThrowIfOutOfCapacity(entity);
        ThrowIfNotFinite(position);
        if (_cellOf[entity.Index] != None)
        {
            throw new InvalidOperationException($"Slot {entity.Index} is already in the spatial index (as {_entities[entity.Index]}).");
        }

        _entities[entity.Index] = entity;
        _positions[entity.Index] = position;
        Link(entity.Index, CellOf(position));
        Count++;
    }

    /// <inheritdoc />
    public void Move(Entity entity, Vector3 position)
    {
        ThrowIfNotFinite(position);
        if (!Contains(entity))
        {
            throw new InvalidOperationException($"{entity} is not in the spatial index.");
        }

        _positions[entity.Index] = position;
        int cell = CellOf(position);
        if (cell != _cellOf[entity.Index])
        {
            Unlink(entity.Index);
            Link(entity.Index, cell);
        }
    }

    /// <inheritdoc />
    public bool Remove(Entity entity)
    {
        if (!Contains(entity))
        {
            return false;
        }

        Unlink(entity.Index);
        Count--;
        return true;
    }

    /// <inheritdoc />
    public bool Contains(Entity entity) =>
        (uint)entity.Index < (uint)_cellOf.Length
        && _cellOf[entity.Index] != None
        && _entities[entity.Index] == entity;

    /// <inheritdoc />
    public SpatialQueryResult QueryRadius(Vector3 center, float radius, Span<Entity> results) =>
        Query(center, radius, Vector2.Zero, cosHalfAngle: -1f, results);

    /// <inheritdoc />
    public SpatialQueryResult QueryCone(Vector3 origin, Vector3 forward, float range, float halfAngleRadians, Span<Entity> results)
    {
        var direction = new Vector2(forward.X, forward.Z);
        if (direction.LengthSquared() == 0f || !float.IsFinite(direction.LengthSquared()))
        {
            throw new ArgumentOutOfRangeException(nameof(forward), forward, "Cone direction needs a non-zero, finite ground-plane component.");
        }

        if (!float.IsFinite(halfAngleRadians) || halfAngleRadians < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(halfAngleRadians), halfAngleRadians, "Half angle must be finite and not negative.");
        }

        return Query(origin, range, Vector2.Normalize(direction), MathF.Cos(MathF.Min(halfAngleRadians, MathF.PI)), results);
    }

    private SpatialQueryResult Query(Vector3 center, float range, Vector2 direction, float cosHalfAngle, Span<Entity> results)
    {
        ThrowIfNotFinite(center);
        if (!float.IsFinite(range) || range < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(range), range, "Range must be finite and not negative.");
        }

        int minCellX = CellCoordinate(center.X - range - _settings.MinX, _settings.CellsX);
        int maxCellX = CellCoordinate(center.X + range - _settings.MinX, _settings.CellsX);
        int minCellZ = CellCoordinate(center.Z - range - _settings.MinZ, _settings.CellsZ);
        int maxCellZ = CellCoordinate(center.Z + range - _settings.MinZ, _settings.CellsZ);
        float rangeSquared = range * range;
        bool isCone = cosHalfAngle > -1f;
        int count = 0;

        for (int cellZ = minCellZ; cellZ <= maxCellZ; cellZ++)
        {
            for (int cellX = minCellX; cellX <= maxCellX; cellX++)
            {
                for (int index = _cellHeads[(cellZ * _settings.CellsX) + cellX]; index != None; index = _next[index])
                {
                    var offset = new Vector2(_positions[index].X - center.X, _positions[index].Z - center.Z);
                    float distanceSquared = offset.LengthSquared();
                    if (distanceSquared > rangeSquared || (isCone && !IsInsideCone(offset, distanceSquared, direction, cosHalfAngle)))
                    {
                        continue;
                    }

                    if (count == results.Length)
                    {
                        return new SpatialQueryResult(count, Truncated: true);
                    }

                    results[count++] = _entities[index];
                }
            }
        }

        return new SpatialQueryResult(count, Truncated: false);
    }

    private static bool IsInsideCone(Vector2 offset, float distanceSquared, Vector2 direction, float cosHalfAngle)
    {
        if (distanceSquared == 0f)
        {
            return true;
        }

        float dot = Vector2.Dot(offset, direction);
        if (cosHalfAngle >= 0f)
        {
            return dot >= 0f && dot * dot >= cosHalfAngle * cosHalfAngle * distanceSquared;
        }

        return dot >= 0f || dot * dot <= cosHalfAngle * cosHalfAngle * distanceSquared;
    }

    private int CellOf(Vector3 position) =>
        (CellCoordinate(position.Z - _settings.MinZ, _settings.CellsZ) * _settings.CellsX)
        + CellCoordinate(position.X - _settings.MinX, _settings.CellsX);

    private int CellCoordinate(float offset, int cells) =>
        Math.Clamp((int)MathF.Floor(offset * _inverseCellSize), 0, cells - 1);

    private void Link(int index, int cell)
    {
        int head = _cellHeads[cell];
        _next[index] = head;
        _previous[index] = None;
        if (head != None)
        {
            _previous[head] = index;
        }

        _cellHeads[cell] = index;
        _cellOf[index] = cell;
    }

    private void Unlink(int index)
    {
        int previous = _previous[index];
        int next = _next[index];
        if (previous != None)
        {
            _next[previous] = next;
        }
        else
        {
            _cellHeads[_cellOf[index]] = next;
        }

        if (next != None)
        {
            _previous[next] = previous;
        }

        _cellOf[index] = None;
    }

    private void ThrowIfOutOfCapacity(Entity entity)
    {
        if ((uint)entity.Index >= (uint)_cellOf.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(entity), entity, $"Entity index exceeds the spatial index capacity {_cellOf.Length}; it must match the ECS capacity.");
        }
    }

    private static void ThrowIfNotFinite(Vector3 position)
    {
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(position), position, "Positions must be finite.");
        }
    }
}
