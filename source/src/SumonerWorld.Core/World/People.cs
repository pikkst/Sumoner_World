using SumonerWorld.Core.Data;
using SumonerWorld.Core.Data.Definitions;

namespace SumonerWorld.Core.World;

/// <summary>
/// Where the Vale's people are (E5a). Someone with a round walks it at <see cref="WalkSpeed"/>, resting <see cref="RestSeconds"/>
/// at each point, and starts over; where they are follows from the world tick alone, so the server (talking range) and every
/// client (drawing them) agree without sending a thing.
/// </summary>
public static class People
{
    /// <summary>Walking speed in metres per second.</summary>
    public const float WalkSpeed = 1.1f;

    /// <summary>Seconds a person rests at each point of their round.</summary>
    public const float RestSeconds = 6f;

    /// <summary>Ticks per second of the world clock.</summary>
    public const float TicksPerSecond = 30f;

    /// <summary>Where <paramref name="person"/> is at <paramref name="tick"/>: x, z and the way they face (degrees, 0 = +Z).</summary>
    public static (float X, float Z, float Facing) At(NpcDefinition person, double tick)
    {
        ArgumentNullException.ThrowIfNull(person);
        IReadOnlyList<RoutePoint> route = person.Route;
        if (route.Count < 2)
        {
            return (person.X, person.Z, person.Facing);
        }

        double cycle = 0;
        for (int i = 0; i < route.Count; i++)
        {
            cycle += RestSeconds + (Length(route[i], route[(i + 1) % route.Count]) / WalkSpeed);
        }

        double t = (tick / TicksPerSecond) % cycle;
        for (int i = 0; i < route.Count; i++)
        {
            RoutePoint from = route[i];
            RoutePoint to = route[(i + 1) % route.Count];
            float facing = MathF.Atan2(to.X - from.X, to.Z - from.Z) * 180f / MathF.PI;
            if (t < RestSeconds)
            {
                return (from.X, from.Z, facing);
            }

            t -= RestSeconds;
            double walk = Length(from, to) / WalkSpeed;
            if (t < walk)
            {
                float share = (float)(t / walk);
                return (from.X + ((to.X - from.X) * share), from.Z + ((to.Z - from.Z) * share), facing);
            }

            t -= walk;
        }

        return (route[0].X, route[0].Z, person.Facing);
    }

    /// <summary>The index in <see cref="IDataRegistry.People"/> of the person within <paramref name="range"/> metres of a point, or −1.</summary>
    public static int Near(IDataRegistry data, float x, float z, double tick, float range)
    {
        ArgumentNullException.ThrowIfNull(data);
        int best = -1;
        float nearest = range;
        for (int i = 0; i < data.People.Count; i++)
        {
            (float px, float pz, float _) = At(data.People[i], tick);
            float distance = MathF.Sqrt(((x - px) * (x - px)) + ((z - pz) * (z - pz)));
            if (distance < nearest)
            {
                (best, nearest) = (i, distance);
            }
        }

        return best;
    }

    /// <summary>The index of the person with this id, or −1.</summary>
    public static int IndexOf(IDataRegistry data, string id)
    {
        ArgumentNullException.ThrowIfNull(data);
        for (int i = 0; i < data.People.Count; i++)
        {
            if (data.People[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    private static float Length(RoutePoint a, RoutePoint b) => MathF.Sqrt(((b.X - a.X) * (b.X - a.X)) + ((b.Z - a.Z) * (b.Z - a.Z)));
}
