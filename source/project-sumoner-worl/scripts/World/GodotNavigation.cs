using Godot;
using SumonerWorld.Core.Navigation;
using SumonerWorld.Core.World;
using NumericsVector3 = System.Numerics.Vector3;

namespace SumonerWorld.Game.World;

/// <summary>
/// <see cref="INavigation"/> on Godot's <c>NavigationServer3D</c> (TDD §2.4): its own navigation map with one region whose mesh is
/// baked from the heightfield (2 m triangles; walkable up to 40° for a 0.5 m radius, 1.75 m tall creature). Path queries are
/// thread-safe on the navigation server, so <see cref="FindPath"/> runs on job workers. Until the first map sync after the bake there
/// is no mesh and every request returns <see cref="PathStatus.NotFound"/>; <see cref="IsReady"/> says when paths work.
/// </summary>
public sealed class GodotNavigation : INavigation, IDisposable
{
    private const float CellSize = 0.5f;
    private const float CellHeight = 0.25f;
    private const int SourceStep = 2;
    private const float ArrivalSlack = 1.5f;

    private readonly Rid _map;
    private readonly Rid _region;

    private GodotNavigation(Rid map, Rid region, int polygons)
    {
        _map = map;
        _region = region;
        Polygons = polygons;
    }

    /// <summary>Polygons in the baked mesh (0 = nothing walkable).</summary>
    public int Polygons { get; }

    /// <summary>True once the map has synced a non-empty mesh.</summary>
    public bool IsReady => Polygons > 0 && NavigationServer3D.MapGetIterationId(_map) > 0;

    /// <summary>Bakes the navigation mesh for <paramref name="field"/> and creates the map (blocking; a few seconds for 512 m).</summary>
    public static GodotNavigation Bake(Heightfield field)
    {
        ArgumentNullException.ThrowIfNull(field);
        var mesh = new NavigationMesh
        {
            CellSize = CellSize,
            CellHeight = CellHeight,
            AgentRadius = 0.5f,
            AgentHeight = 1.75f,
            AgentMaxClimb = 0.5f,
            AgentMaxSlope = 40f,
        };
        var source = new NavigationMeshSourceGeometryData3D();
        source.AddFaces(Faces(field), Transform3D.Identity);
        NavigationServer3D.BakeFromSourceGeometryData(mesh, source);

        Rid map = NavigationServer3D.MapCreate();
        NavigationServer3D.MapSetCellSize(map, CellSize);
        NavigationServer3D.MapSetCellHeight(map, CellHeight);
        NavigationServer3D.MapSetActive(map, true);
        Rid region = NavigationServer3D.RegionCreate();
        NavigationServer3D.RegionSetMap(region, map);
        NavigationServer3D.RegionSetNavigationMesh(region, mesh);
        return new GodotNavigation(map, region, mesh.GetPolygonCount());
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
        NavigationServer3D.FreeRid(_region);
        NavigationServer3D.FreeRid(_map);
    }

    private static Vector3[] Faces(Heightfield field)
    {
        int cells = (field.Samples - 1) / SourceStep;
        var faces = new Vector3[cells * cells * 6];
        int f = 0;
        for (int j = 0; j < cells; j++)
        {
            for (int i = 0; i < cells; i++)
            {
                Vector3 a = Corner(field, i, j);
                Vector3 b = Corner(field, i + 1, j);
                Vector3 c = Corner(field, i, j + 1);
                Vector3 d = Corner(field, i + 1, j + 1);
                (faces[f], faces[f + 1], faces[f + 2]) = (a, b, c);
                (faces[f + 3], faces[f + 4], faces[f + 5]) = (b, d, c);
                f += 6;
            }
        }

        return faces;
    }

    private static Vector3 Corner(Heightfield field, int i, int j)
    {
        float x = i * SourceStep * field.Spacing;
        float z = j * SourceStep * field.Spacing;
        return new Vector3(x, field.HeightAt(x, z), z);
    }

    private static Vector3 ToGodot(NumericsVector3 v) => new(v.X, v.Y, v.Z);

    private static NumericsVector3 ToNumerics(Vector3 v) => new(v.X, v.Y, v.Z);
}
