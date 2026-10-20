using System.Numerics;

namespace SumonerWorld.Core.World;

/// <summary>A 64 m × 64 m world chunk (TDD §5.1), by index along X and Z; chunk (0, 0) starts at the world origin.</summary>
/// <param name="X">Index along X.</param>
/// <param name="Z">Index along Z.</param>
public readonly record struct ChunkCoord(int X, int Z)
{
    /// <summary>Chunk edge length in metres.</summary>
    public const float Size = 64f;

    /// <summary>The chunk that contains <paramref name="position"/>.</summary>
    public static ChunkCoord Containing(Vector3 position) =>
        new((int)MathF.Floor(position.X / Size), (int)MathF.Floor(position.Z / Size));

    /// <summary>Centre of the chunk at height 0.</summary>
    public Vector3 Centre => new((X + 0.5f) * Size, 0, (Z + 0.5f) * Size);
}

/// <summary>How much of a chunk the client shows (TDD §5.2). Higher values show more.</summary>
public enum StreamRing : byte
{
    /// <summary>Nothing but what the terrain itself draws.</summary>
    None = 0,

    /// <summary>Terrain LOD and impostors (out to about 1.5 km).</summary>
    Far = 1,

    /// <summary>LOD1–2 meshes and simplified foliage (out to 6 chunks).</summary>
    Medium = 2,

    /// <summary>Full meshes and collision (out to 2 chunks).</summary>
    Full = 3,
}

/// <summary>One chunk changing ring.</summary>
/// <param name="Chunk">The chunk.</param>
/// <param name="From">Ring it had.</param>
/// <param name="To">Ring it should have.</param>
public readonly record struct ChunkChange(ChunkCoord Chunk, StreamRing From, StreamRing To);

/// <summary>Settings of a <see cref="StreamingPlanner"/>.</summary>
public sealed class StreamingSettings
{
    /// <summary>Distance from the viewer to a chunk's nearest point within which the chunk is <see cref="StreamRing.Full"/> (2 chunks).</summary>
    public float FullRadius { get; init; } = 2 * ChunkCoord.Size;

    /// <summary>Same for <see cref="StreamRing.Medium"/> (6 chunks).</summary>
    public float MediumRadius { get; init; } = 6 * ChunkCoord.Size;

    /// <summary>Same for <see cref="StreamRing.Far"/> (1.5 km).</summary>
    public float FarRadius { get; init; } = 1536f;

    /// <summary>A chunk keeps its ring until it is this much farther out, so walking along a ring edge does not load and unload.</summary>
    public float Hysteresis { get; init; } = 16f;

    /// <summary>Most ring upgrades started per update; the nearest first. Downgrades are not limited (they free memory).</summary>
    public int MaxUpgradesPerUpdate { get; init; } = 4;

    /// <summary>First chunk of the region (inclusive).</summary>
    public ChunkCoord RegionMin { get; init; }

    /// <summary>Chunk count along X and Z (the M1 test region: 8 × 8 = 512 m).</summary>
    public int RegionChunks { get; init; } = 8;
}

/// <summary>Decides which ring each chunk should have for a viewer position (TDD §5.2); the Game layer loads and unloads content.</summary>
public interface IStreamingPlanner
{
    /// <summary>The ring a chunk has now.</summary>
    StreamRing RingOf(ChunkCoord chunk);

    /// <summary>Plans the changes for a viewer at <paramref name="viewer"/> into <paramref name="changes"/> and applies them to the state.</summary>
    void Update(Vector3 viewer, List<ChunkChange> changes);
}

/// <summary>
/// Client streaming rings (TDD §5.2). Distances are measured from the viewer to the nearest point of each chunk, in the XZ plane.
/// Each <see cref="Update"/> returns every downgrade and at most <see cref="StreamingSettings.MaxUpgradesPerUpdate"/> upgrades, the
/// nearest chunks first, so a fast viewer never asks for more loading in one frame than the budget allows; the rest follow in
/// later updates. A chunk is only downgraded once it is <see cref="StreamingSettings.Hysteresis"/> beyond its ring.
/// Allocation-free after construction when <c>changes</c> has capacity.
/// </summary>
public sealed class StreamingPlanner : IStreamingPlanner
{
    private readonly StreamingSettings _settings;
    private readonly StreamRing[] _rings;
    private readonly Candidate[] _upgrades;

    /// <summary>Creates a planner with every chunk at <see cref="StreamRing.None"/>.</summary>
    public StreamingPlanner(StreamingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(settings.RegionChunks);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(settings.MaxUpgradesPerUpdate);
        if (!(settings.FullRadius <= settings.MediumRadius && settings.MediumRadius <= settings.FarRadius))
        {
            throw new ArgumentException("Ring radii must grow: full ≤ medium ≤ far.", nameof(settings));
        }

        _settings = settings;
        _rings = new StreamRing[settings.RegionChunks * settings.RegionChunks];
        _upgrades = new Candidate[_rings.Length];
    }

    /// <summary>Chunks in the region.</summary>
    public int ChunkCount => _rings.Length;

    /// <summary>Chunks with each ring now, indexed by <see cref="StreamRing"/>.</summary>
    public int Count(StreamRing ring)
    {
        int count = 0;
        foreach (StreamRing current in _rings)
        {
            count += current == ring ? 1 : 0;
        }

        return count;
    }

    /// <inheritdoc />
    public StreamRing RingOf(ChunkCoord chunk) => TryIndex(chunk, out int index) ? _rings[index] : StreamRing.None;

    /// <inheritdoc />
    public void Update(Vector3 viewer, List<ChunkChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        changes.Clear();
        int upgrades = 0;
        for (int index = 0; index < _rings.Length; index++)
        {
            ChunkCoord chunk = ChunkAt(index);
            float distance = DistanceToChunk(viewer, chunk);
            StreamRing current = _rings[index];
            StreamRing wanted = RingFor(distance);
            if (wanted > current)
            {
                _upgrades[upgrades++] = new Candidate(index, distance, wanted);
            }
            else if (wanted < current)
            {
                // Downgrade only as far as the hysteresis allows (a chunk just past the Full edge stays Full a little longer).
                StreamRing kept = RingFor(MathF.Max(0, distance - _settings.Hysteresis));
                StreamRing target = kept < current ? kept : current;
                if (target < current)
                {
                    changes.Add(new ChunkChange(chunk, current, target));
                    _rings[index] = target;
                }
            }
        }

        // Nearest first; partial selection keeps this O(n · budget) and allocation-free.
        int budget = Math.Min(upgrades, _settings.MaxUpgradesPerUpdate);
        for (int i = 0; i < budget; i++)
        {
            int nearest = i;
            for (int j = i + 1; j < upgrades; j++)
            {
                if (_upgrades[j].Distance < _upgrades[nearest].Distance)
                {
                    nearest = j;
                }
            }

            (_upgrades[i], _upgrades[nearest]) = (_upgrades[nearest], _upgrades[i]);
            Candidate pick = _upgrades[i];
            changes.Add(new ChunkChange(ChunkAt(pick.Index), _rings[pick.Index], pick.Wanted));
            _rings[pick.Index] = pick.Wanted;
        }
    }

    /// <summary>Upgrades still waiting after the last update (0 once streaming has caught up).</summary>
    public int Pending(Vector3 viewer)
    {
        int pending = 0;
        for (int index = 0; index < _rings.Length; index++)
        {
            pending += RingFor(DistanceToChunk(viewer, ChunkAt(index))) > _rings[index] ? 1 : 0;
        }

        return pending;
    }

    private static float DistanceToChunk(Vector3 viewer, ChunkCoord chunk)
    {
        float minX = chunk.X * ChunkCoord.Size;
        float minZ = chunk.Z * ChunkCoord.Size;
        float dx = MathF.Max(MathF.Max(minX - viewer.X, 0), viewer.X - (minX + ChunkCoord.Size));
        float dz = MathF.Max(MathF.Max(minZ - viewer.Z, 0), viewer.Z - (minZ + ChunkCoord.Size));
        return MathF.Sqrt((dx * dx) + (dz * dz));
    }

    private StreamRing RingFor(float distance) =>
        distance <= _settings.FullRadius ? StreamRing.Full
        : distance <= _settings.MediumRadius ? StreamRing.Medium
        : distance <= _settings.FarRadius ? StreamRing.Far
        : StreamRing.None;

    private ChunkCoord ChunkAt(int index) =>
        new(_settings.RegionMin.X + (index % _settings.RegionChunks), _settings.RegionMin.Z + (index / _settings.RegionChunks));

    private bool TryIndex(ChunkCoord chunk, out int index)
    {
        int x = chunk.X - _settings.RegionMin.X;
        int z = chunk.Z - _settings.RegionMin.Z;
        bool inside = (uint)x < (uint)_settings.RegionChunks && (uint)z < (uint)_settings.RegionChunks;
        index = inside ? (z * _settings.RegionChunks) + x : -1;
        return inside;
    }

    private readonly record struct Candidate(int Index, float Distance, StreamRing Wanted);
}
