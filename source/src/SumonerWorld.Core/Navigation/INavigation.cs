using System.Numerics;

namespace SumonerWorld.Core.Navigation;

/// <summary>How a path search ended.</summary>
public enum PathStatus : byte
{
    /// <summary>A path to the goal.</summary>
    Found,

    /// <summary>The goal is unreachable; the path ends at the closest reachable point.</summary>
    Partial,

    /// <summary>No path (start off the navigation mesh, or no mesh yet).</summary>
    NotFound,
}

/// <summary>
/// Path finding (TDD §2.4). Implementations must be thread-safe: Core calls <see cref="FindPath"/> from job workers (<see cref="PathPlanner"/>),
/// and the results enter the simulation at the next tick's job stage in a fixed order (R11, R17). The Godot adapter uses
/// <c>NavigationServer3D</c>; tests and Godot-free hosts use <see cref="StraightLineNavigation"/>.
/// </summary>
public interface INavigation
{
    /// <summary>
    /// Writes the waypoints after <paramref name="start"/> up to and including the end point into <paramref name="waypoints"/>; when the
    /// path has more corners than fit, the last slot holds the end point and the earlier corners are kept.
    /// </summary>
    PathStatus FindPath(Vector3 start, Vector3 goal, Span<Vector3> waypoints, out int count);
}

/// <summary>Open ground everywhere: the path is the goal itself. For tests, benchmarks and hosts without a navigation mesh.</summary>
public sealed class StraightLineNavigation : INavigation
{
    /// <summary>The shared instance.</summary>
    public static readonly StraightLineNavigation Instance = new();

    /// <inheritdoc />
    public PathStatus FindPath(Vector3 start, Vector3 goal, Span<Vector3> waypoints, out int count)
    {
        if (waypoints.IsEmpty)
        {
            count = 0;
            return PathStatus.NotFound;
        }

        waypoints[0] = goal;
        count = 1;
        return PathStatus.Found;
    }
}
