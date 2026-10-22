using Godot;
using SumonerWorld.Core.Navigation;
using SumonerWorld.Core.World;
using NumericsVector3 = System.Numerics.Vector3;

namespace SumonerWorld.Game.World;

/// <summary>
/// <see cref="INavigation"/> on Godot's <c>NavigationServer3D</c> (TDD §2.4): its own navigation map with one region per
/// <see cref="TileSize"/> tile, each baked from its part of the heightfield (2 m triangles; walkable up to 40° for a 0.5 m radius,
/// 1.75 m tall creature). Tiles keep the bake's memory small on the 2 km region (E4a); their borders line up, so the map joins them.
/// The tiles around a focus point (the spawn) are baked before <see cref="Bake"/> returns, the rest on a worker thread, nearest
/// first, so the server starts in seconds and far tiles join the map as they are done. Path queries are
/// thread-safe on the navigation server, so <see cref="FindPath"/> runs on job workers. Until the first map sync after the bake there
/// is no mesh and every request returns <see cref="PathStatus.NotFound"/>; <see cref="IsReady"/> says when paths work.
/// </summary>
public sealed class GodotNavigation : INavigation, IDisposable
{
    private const float CellSize = 0.5f;
    private const float CellHeight = 0.25f;
    private const int SourceStep = 2;
    private const float ArrivalSlack = 1.5f;
    private const float Border = 4f;

    /// <summary>Metres per side of a baked tile.</summary>
    public const float TileSize = 256f;

    private readonly Rid _map;
    private readonly List<Rid> _regions = [];
    private readonly CancellationTokenSource _stop = new();
    private Task? _rest;
    private int _polygons;

    private GodotNavigation(Rid map) => _map = map;

    /// <summary>Polygons baked so far (0 = nothing walkable).</summary>
    public int Polygons => Volatile.Read(ref _polygons);

    /// <summary>Tiles baked so far.</summary>
    public int TilesBaked { get; private set; }

    /// <summary>Tiles in all.</summary>
    public int TileCount { get; private set; }

    /// <summary>True once the map has synced a non-empty mesh.</summary>
    public bool IsReady => Polygons > 0 && NavigationServer3D.MapGetIterationId(_map) > 0;

    /// <summary>
    /// Creates the map for <paramref name="field"/>: bakes the tiles within <paramref name="nearMetres"/> of <paramref name="focus"/>
    /// now (about half a second a tile) and the others in the background, nearest first.
    /// </summary>
    public static GodotNavigation Bake(Heightfield field, NumericsVector3 focus, float nearMetres = 300f)
    {
        ArgumentNullException.ThrowIfNull(field);
        Rid map = NavigationServer3D.MapCreate();
        NavigationServer3D.MapSetCellSize(map, CellSize);
        NavigationServer3D.MapSetCellHeight(map, CellHeight);
        NavigationServer3D.MapSetActive(map, true);
        var navigation = new GodotNavigation(map);
        int tiles = (int)MathF.Ceiling(field.Size / TileSize);
        var order = new List<(float Distance, float X, float Z)>();
        for (int tz = 0; tz < tiles; tz++)
        {
            for (int tx = 0; tx < tiles; tx++)
            {
                float x = (tx + 0.5f) * TileSize;
                float z = (tz + 0.5f) * TileSize;
                order.Add((new Vector2(x - focus.X, z - focus.Z).Length(), tx * TileSize, tz * TileSize));
            }
        }

        order.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        navigation.TileCount = order.Count;
        int now = Math.Max(1, order.Count(tile => tile.Distance <= nearMetres));
        foreach ((float _, float x, float z) in order.Take(now))
        {
            navigation.Add(BakeTile(field, x, z));
        }

        CancellationToken stop = navigation._stop.Token;
        navigation._rest = Task.Run(
            () =>
            {
                foreach ((float _, float x, float z) in order.Skip(now))
                {
                    if (stop.IsCancellationRequested)
                    {
                        return;
                    }

                    navigation.Add(BakeTile(field, x, z));
                }
            },
            stop);
        return navigation;
    }

    // Puts a baked tile on the map (the navigation server queues the change, so this is safe from the worker).
    private void Add(NavigationMesh mesh)
    {
        Rid region = NavigationServer3D.RegionCreate();
        NavigationServer3D.RegionSetMap(region, _map);
        NavigationServer3D.RegionSetNavigationMesh(region, mesh);
        lock (_regions)
        {
            _regions.Add(region);
            TilesBaked++;
        }

        Interlocked.Add(ref _polygons, mesh.GetPolygonCount());
    }

    // One tile: the ground a little past its edges is baked, and the mesh is cut at the tile's edges so neighbours meet.
    private static NavigationMesh BakeTile(Heightfield field, float x0, float z0)
    {
        var mesh = new NavigationMesh
        {
            CellSize = CellSize,
            CellHeight = CellHeight,
            AgentRadius = 0.5f,
            AgentHeight = 1.75f,
            AgentMaxClimb = 0.5f,
            AgentMaxSlope = 40f,
            BorderSize = Border,
            FilterBakingAabb = new Aabb(new Vector3(x0, -1000, z0), new Vector3(TileSize, 4000, TileSize)),
        };
        var source = new NavigationMeshSourceGeometryData3D();
        source.AddFaces(Faces(field, x0 - Border, z0 - Border, TileSize + (2 * Border)), Transform3D.Identity);
        NavigationServer3D.BakeFromSourceGeometryData(mesh, source);
        return mesh;
    }

    /// <inheritdoc />
    public PathStatus FindPath(NumericsVector3 start, NumericsVector3 goal, Span<NumericsVector3> waypoints, out int count)
    {
        count = 0;
        if (waypoints.IsEmpty)
        {
            return PathStatus.NotFound;
        }

        Vector3[] path = NavigationServer3D.MapGetPath(_map, ToGodot(start), ToGodot(goal), optimize: true);
        if (path.Length < 2)
        {
            return PathStatus.NotFound;
        }

        // path[0] is the start; keep the corners after it, and always the end point in the last slot.
        int corners = path.Length - 1;
        int keep = Math.Min(corners, waypoints.Length);
        for (int i = 0; i < keep - 1; i++)
        {
            waypoints[i] = ToNumerics(path[i + 1]);
        }

        Vector3 end = path[^1];
        waypoints[keep - 1] = ToNumerics(end);
        count = keep;
        float missed = new Vector2(end.X - goal.X, end.Z - goal.Z).Length();
        return missed <= ArrivalSlack ? PathStatus.Found : PathStatus.Partial;
    }

    /// <summary>Frees the map and region.</summary>
    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _rest?.Wait();
        }
        catch (AggregateException)
        {
            // Cancelled before it started.
        }

        lock (_regions)
        {
            foreach (Rid region in _regions)
            {
                NavigationServer3D.FreeRid(region);
            }
        }

        NavigationServer3D.FreeRid(_map);
        _stop.Dispose();
    }

    // The ground of a square (clamped to the field) as triangles every SourceStep metres.
    private static Vector3[] Faces(Heightfield field, float x0, float z0, float size)
    {
        float left = MathF.Max(0, x0);
        float bottom = MathF.Max(0, z0);
        int columns = (int)((MathF.Min(field.Size, x0 + size) - left) / SourceStep);
        int rows = (int)((MathF.Min(field.Size, z0 + size) - bottom) / SourceStep);
        var faces = new Vector3[Math.Max(0, columns) * Math.Max(0, rows) * 6];
        int f = 0;
        for (int j = 0; j < rows; j++)
        {
            for (int i = 0; i < columns; i++)
            {
                Vector3 a = Corner(field, left, bottom, i, j);
                Vector3 b = Corner(field, left, bottom, i + 1, j);
                Vector3 c = Corner(field, left, bottom, i, j + 1);
                Vector3 d = Corner(field, left, bottom, i + 1, j + 1);
                (faces[f], faces[f + 1], faces[f + 2]) = (a, b, c);
                (faces[f + 3], faces[f + 4], faces[f + 5]) = (b, d, c);
                f += 6;
            }
        }

        return faces;
    }

    private static Vector3 Corner(Heightfield field, float left, float bottom, int i, int j)
    {
        float x = left + (i * SourceStep);
        float z = bottom + (j * SourceStep);
        return new Vector3(x, field.HeightAt(x, z), z);
    }

    private static Vector3 ToGodot(NumericsVector3 v) => new(v.X, v.Y, v.Z);

    private static NumericsVector3 ToNumerics(Vector3 v) => new(v.X, v.Y, v.Z);
}
