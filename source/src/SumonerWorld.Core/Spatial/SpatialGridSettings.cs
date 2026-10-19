namespace SumonerWorld.Core.Spatial;

/// <summary>
/// Bounds and resolution of the spatial grid (AI Bible §9.3, TDD §5.1). Loaded from world config, not hard-coded (R48).
/// The grid covers the ground plane (X/Z); height is stored but does not affect which cell an entity is in.
/// </summary>
public sealed class SpatialGridSettings
{
    /// <summary>Cell edge length from AI Bible §9.3.</summary>
    public const float DefaultCellSize = 16f;

    /// <summary>Edge length of the early-access region (TDD §5.1).</summary>
    public const float DefaultWorldSize = 4096f;

    /// <summary>Creates validated settings.</summary>
    /// <param name="minX">World X of the grid's west edge, in metres.</param>
    /// <param name="minZ">World Z of the grid's south edge, in metres.</param>
    /// <param name="sizeX">Grid extent along X, in metres.</param>
    /// <param name="sizeZ">Grid extent along Z, in metres.</param>
    /// <param name="cellSize">Cell edge length, in metres.</param>
    /// <param name="entityCapacity">Must match the ECS world capacity (entity indices are used directly).</param>
    public SpatialGridSettings(float minX, float minZ, float sizeX, float sizeZ, float cellSize, int entityCapacity)
    {
        if (!float.IsFinite(minX) || !float.IsFinite(minZ))
        {
            throw new ArgumentOutOfRangeException(nameof(minX), "Grid origin must be finite.");
        }

        ThrowIfNotPositiveFinite(sizeX, nameof(sizeX));
        ThrowIfNotPositiveFinite(sizeZ, nameof(sizeZ));
        ThrowIfNotPositiveFinite(cellSize, nameof(cellSize));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(entityCapacity);

        MinX = minX;
        MinZ = minZ;
        CellSize = cellSize;
        CellsX = (int)MathF.Ceiling(sizeX / cellSize);
        CellsZ = (int)MathF.Ceiling(sizeZ / cellSize);
        EntityCapacity = entityCapacity;
    }

    /// <summary>World X of the west edge.</summary>
    public float MinX { get; }

    /// <summary>World Z of the south edge.</summary>
    public float MinZ { get; }

    /// <summary>Cell edge length in metres.</summary>
    public float CellSize { get; }

    /// <summary>Number of cells along X.</summary>
    public int CellsX { get; }

    /// <summary>Number of cells along Z.</summary>
    public int CellsZ { get; }

    /// <summary>Highest entity index + 1 that can be stored.</summary>
    public int EntityCapacity { get; }

    /// <summary>Settings for the early-access region centred on the origin.</summary>
    public static SpatialGridSettings ForRegion(int entityCapacity) =>
        new(-DefaultWorldSize / 2, -DefaultWorldSize / 2, DefaultWorldSize, DefaultWorldSize, DefaultCellSize, entityCapacity);

    private static void ThrowIfNotPositiveFinite(float value, string name)
    {
        if (!float.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(name, value, "Must be a positive, finite number of metres.");
        }
    }
}
