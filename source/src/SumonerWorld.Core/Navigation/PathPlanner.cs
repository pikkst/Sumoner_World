using System.Numerics;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Jobs;

namespace SumonerWorld.Core.Navigation;

/// <summary>Receives finished paths on the simulation thread.</summary>
public interface IPathSink
{
    /// <summary>A path for <paramref name="entity"/> is ready (the entity is alive). <paramref name="waypoints"/> is only valid during the call.</summary>
    void OnPath(Entity entity, PathStatus status, ReadOnlySpan<Vector3> waypoints, float speed);
}

/// <summary>
/// Asynchronous path requests (TDD §2.4, AI Bible §9.3): requests made during a tick are searched together as one job on a worker
/// (<see cref="INavigation.FindPath"/>) and handed to the <see cref="IPathSink"/> at the next tick's job stage, in request order, so
/// the outcome does not depend on thread timing (R11, R17). A tree that asked for a path waits; the tick never blocks. Fixed
/// capacity, no allocation per request (R21); a full planner refuses requests instead of growing.
/// </summary>
public sealed class PathPlanner : IJob
{
    /// <summary>Most waypoints kept per path.</summary>
    public const int MaxWaypoints = 8;

    private readonly INavigation _navigation;
    private readonly IEcsWorld _world;
    private readonly PathRequest[] _requests;
    private readonly Vector3[] _waypoints;
    private readonly int[] _counts;
    private readonly PathStatus[] _statuses;
    private int _count;
    private bool _submitted;

    /// <summary>Creates a planner for up to <paramref name="capacity"/> requests per tick.</summary>
    public PathPlanner(INavigation navigation, IEcsWorld world, int capacity)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _navigation = navigation;
        _world = world;
        _requests = new PathRequest[capacity];
        _waypoints = new Vector3[capacity * MaxWaypoints];
        _counts = new int[capacity];
        _statuses = new PathStatus[capacity];
    }

    /// <summary>Where finished paths go (set once while the world is built).</summary>
    public IPathSink? Sink { get; set; }

    /// <summary>Requests waiting for this tick's job.</summary>
    public int Waiting => _count;

    /// <summary>Paths delivered since the start.</summary>
    public long Delivered { get; private set; }

    /// <summary>Delivered paths with <see cref="PathStatus.NotFound"/>.</summary>
    public long NotFound { get; private set; }

    /// <summary>Requests refused because the planner was full.</summary>
    public long Refused { get; private set; }

    /// <summary>Asks for a path; false (and counted) when this tick's requests are full or the job is already submitted.</summary>
    public bool Request(Entity entity, Vector3 from, Vector3 to, float speed)
    {
        if (_count == _requests.Length || _submitted)
        {
            Refused++;
            return false;
        }

        _requests[_count++] = new PathRequest(entity, from, to, speed);
        return true;
    }

    /// <summary>Submits this tick's requests as one job (call once per tick after the requests; nothing happens when there are none).</summary>
    public void Submit(IJobScheduler jobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        if (_count > 0 && !_submitted)
        {
            _submitted = true;
            jobs.Submit(this);
        }
    }

    /// <inheritdoc />
    public void Execute()
    {
        for (int i = 0; i < _count; i++)
        {
            PathRequest request = _requests[i];
            Span<Vector3> slots = _waypoints.AsSpan(i * MaxWaypoints, MaxWaypoints);
            _statuses[i] = _navigation.FindPath(request.From, request.To, slots, out int count);
            _counts[i] = Math.Clamp(count, 0, MaxWaypoints);
        }
    }

    /// <inheritdoc />
    public void Apply()
    {
        for (int i = 0; i < _count; i++)
        {
            PathRequest request = _requests[i];
            if (_world.IsAlive(request.Entity))
            {
                Sink?.OnPath(request.Entity, _statuses[i], _waypoints.AsSpan(i * MaxWaypoints, _counts[i]), request.Speed);
                Delivered++;
                NotFound += _statuses[i] == PathStatus.NotFound ? 1 : 0;
            }
        }

        _count = 0;
        _submitted = false;
    }

    private readonly record struct PathRequest(Entity Entity, Vector3 From, Vector3 To, float Speed);
}
