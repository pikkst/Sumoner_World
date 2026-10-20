using Godot;
using SumonerWorld.Core.Physics;
using SumonerWorld.Core.World;
using NumericsVector3 = System.Numerics.Vector3;

namespace SumonerWorld.Game.World;

/// <summary>
/// <see cref="IPhysicsQueries"/> on Godot's physics server (Jolt, project setting; TDD §2.4), in the 3D world of <c>node</c>. Call on the
/// main thread from the simulation tick. Godot returns ray results as a dictionary, so each query allocates; nothing in the per-tick
/// path calls it yet (line of sight for perception will, then a pooled query object is the fix).
/// </summary>
public sealed class GodotPhysicsQueries(Node3D node) : IPhysicsQueries, IDisposable
{
    private readonly PhysicsRayQueryParameters3D _query = new();

    /// <inheritdoc />
    public bool Raycast(NumericsVector3 origin, NumericsVector3 direction, float maxDistance, out RayHit hit)
    {
        hit = default;
        float length = direction.Length();
        if (length == 0 || maxDistance <= 0)
        {
            return false;
        }

        _query.From = ToGodot(origin);
        _query.To = ToGodot(origin + (direction / length * maxDistance));
        Godot.Collections.Dictionary result = node.GetWorld3D().DirectSpaceState.IntersectRay(_query);
        if (result.Count == 0)
        {
            return false;
        }

        Vector3 point = result["position"].AsVector3();
        Vector3 normal = result["normal"].AsVector3();
        var hitPoint = new NumericsVector3(point.X, point.Y, point.Z);
        hit = new RayHit(hitPoint, new NumericsVector3(normal.X, normal.Y, normal.Z), NumericsVector3.Distance(origin, hitPoint));
        return true;
    }

    /// <summary>Frees the reusable query object.</summary>
    public void Dispose() => _query.Dispose();

    /// <inheritdoc />
    public bool IsClear(NumericsVector3 eye, NumericsVector3 target)
    {
        NumericsVector3 delta = target - eye;
        float length = delta.Length();
        return length == 0 || !Raycast(eye, delta, length, out _);
    }

    /// <summary>
    /// Casts 64 rays straight down on an 8 × 8 grid over the field and compares the hits with its heights (server start-up check):
    /// rays that missed and the largest height difference in metres.
    /// </summary>
    public (int Misses, float Worst) CompareWith(Heightfield field)
    {
        ArgumentNullException.ThrowIfNull(field);
        float worst = 0;
        int misses = 0;
        float step = (field.Size - 16) / 7f;
        for (int i = 0; i < 64; i++)
        {
            float x = 8 + (i % 8 * step);
            float z = 8 + (i / 8 * step);
            if (Raycast(new NumericsVector3(x, 200, z), -NumericsVector3.UnitY, 400, out RayHit hit))
            {
                worst = Math.Max(worst, Math.Abs(hit.Point.Y - field.HeightAt(x, z)));
            }
            else
            {
                misses++;
            }
        }

        return (misses, worst);
    }

    /// <summary>
    /// Server-side terrain collision: one static body with a <see cref="HeightMapShape3D"/> of the heightfield (the headless server
    /// does not load Terrain3D, whose collision follows a camera).
    /// </summary>
    public static StaticBody3D AddTerrainCollision(Node parent, Heightfield field)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(field);
        if (field.Spacing != 1f)
        {
            // Scaled collision shapes are not supported by Jolt; the M1 region uses 1 m spacing.
            throw new NotSupportedException($"Terrain collision needs 1 m sample spacing, got {field.Spacing} m.");
        }

        var shape = new HeightMapShape3D { MapWidth = field.Samples, MapDepth = field.Samples, MapData = field.Heights.ToArray() };

        // A height map shape is centred on its body; move it to cover 0…size on X and Z.
        float half = field.Size / 2;
        var body = new StaticBody3D { Name = "TerrainCollision", Position = new Vector3(half, 0, half) };
        body.AddChild(new CollisionShape3D { Shape = shape });
        parent.AddChild(body);
        return body;
    }

    private static Vector3 ToGodot(NumericsVector3 v) => new(v.X, v.Y, v.Z);
}
