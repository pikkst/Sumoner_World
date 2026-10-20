using System.Numerics;
using SumonerWorld.Core.World;

namespace SumonerWorld.Core.Physics;

/// <summary>Where a ray hit.</summary>
/// <param name="Point">The hit point.</param>
/// <param name="Normal">Surface normal at the hit.</param>
/// <param name="Distance">Distance from the ray origin.</param>
public readonly record struct RayHit(Vector3 Point, Vector3 Normal, float Distance);

/// <summary>
/// Physics questions Core may ask (TDD §2.4): Core owns no physics engine. The Godot adapter answers them with Jolt on the same
/// collision data on the server and the client; tests and Godot-free hosts use <see cref="HeightfieldPhysics"/>. Called on the
/// simulation thread only. Ground height for walking does not come from here but from the shared <see cref="Heightfield"/>, so
/// client prediction and the server agree exactly.
/// </summary>
public interface IPhysicsQueries
{
    /// <summary>Casts a ray; <paramref name="direction"/> need not be normalized. False when nothing is hit within <paramref name="maxDistance"/>.</summary>
    bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out RayHit hit);

    /// <summary>True when nothing solid lies on the straight line between two points (line of sight).</summary>
    bool IsClear(Vector3 eye, Vector3 target);
}

/// <summary>
/// <see cref="IPhysicsQueries"/> against terrain only: a <see cref="Heightfield"/>, or flat ground at height 0 without one. Rays are
/// marched in steps of half the sample spacing and the crossing is refined by bisection, so hits are within a few millimetres of the
/// bilinear surface.
/// </summary>
public sealed class HeightfieldPhysics(Heightfield? field) : IPhysicsQueries
{
    private const int RefineSteps = 12;
    private const float NormalProbe = 0.5f;

    private readonly float _step = field is null ? 1f : field.Spacing * 0.5f;

    /// <inheritdoc />
    public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out RayHit hit)
    {
        hit = default;
        float length = direction.Length();
        if (length == 0 || maxDistance <= 0)
        {
            return false;
        }

        Vector3 unit = direction / length;
        float previous = 0;
        float previousAbove = Above(origin);
        if (previousAbove <= 0)
        {
            hit = new RayHit(origin, Normal(origin.X, origin.Z), 0);
            return true;
        }

        for (float t = _step; previous < maxDistance; t += _step)
        {
            float distance = MathF.Min(t, maxDistance);
            float above = Above(origin + (unit * distance));
            if (above <= 0)
            {
                float lo = previous;
                float hi = distance;
                for (int i = 0; i < RefineSteps; i++)
                {
                    float mid = (lo + hi) * 0.5f;
                    (lo, hi) = Above(origin + (unit * mid)) > 0 ? (mid, hi) : (lo, mid);
                }

                Vector3 point = origin + (unit * hi);
                hit = new RayHit(point, Normal(point.X, point.Z), hi);
                return true;
            }

            previous = distance;
        }

        return false;
    }

    /// <inheritdoc />
    public bool IsClear(Vector3 eye, Vector3 target)
    {
        Vector3 delta = target - eye;
        float length = delta.Length();
        return length == 0 || !Raycast(eye, delta, length, out _);
    }

    private float Height(float x, float z) => field?.HeightAt(x, z) ?? 0f;

    private float Above(Vector3 point) => point.Y - Height(point.X, point.Z);

    private Vector3 Normal(float x, float z)
    {
        float dx = Height(x + NormalProbe, z) - Height(x - NormalProbe, z);
        float dz = Height(x, z + NormalProbe) - Height(x, z - NormalProbe);
        return Vector3.Normalize(new Vector3(-dx, 2 * NormalProbe, -dz));
    }
}
