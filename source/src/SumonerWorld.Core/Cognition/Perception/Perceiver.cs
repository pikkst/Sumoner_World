using System.Numerics;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Cognition.Opinions;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Spatial;

namespace SumonerWorld.Core.Cognition.Perception;

/// <summary>How something was noticed. Sight wins when both apply.</summary>
public enum Sense : byte
{
    /// <summary>Seen inside the view cone.</summary>
    Sight,

    /// <summary>Heard nearby (any direction).</summary>
    Hearing,
}

/// <summary>One noticed entity.</summary>
/// <param name="Entity">What was noticed.</param>
/// <param name="DistanceCm">Ground-plane distance in centimetres (integer for determinism).</param>
/// <param name="Sense">How it was noticed.</param>
public readonly record struct Percept(Entity Entity, int DistanceCm, Sense Sense);

/// <summary>Maps a perceived entity to the subject memories and opinions are about (player account or creature id).</summary>
public interface ISubjectResolver
{
    /// <summary>The entity's memory subject; false if it is nothing a creature forms opinions about.</summary>
    bool TryResolve(Entity entity, out MemorySubject subject);
}

/// <summary>The subject a creature pays attention to.</summary>
/// <param name="Entity">The focused entity.</param>
/// <param name="Subject">Its memory subject.</param>
/// <param name="Distance">Distance normalised to 0 (touching) … 1000 (edge of the creature's perception).</param>
public readonly record struct FocusChoice(Entity Entity, MemorySubject Subject, int Distance);

/// <summary>Senses nearby entities (AI Bible §2, R15: a creature only knows what it perceives).</summary>
public interface IPerception
{
    /// <summary>
    /// Writes what <paramref name="self"/> notices into <paramref name="results"/> (nearest first, self excluded) and returns how
    /// many. Sight uses the species' view cone around <paramref name="forward"/>; hearing is all around.
    /// </summary>
    int Perceive(Entity self, Vector3 position, Vector3 forward, PerceptionDefinition senses, Span<Percept> results);

    /// <summary>
    /// Picks the most notable percept (AI Bible §6.2 "nearest notable subject"): players first, then subjects the creature feels
    /// strongly about, then the nearest. Returns false if nothing perceived is a subject.
    /// </summary>
    bool SelectFocus(MindHandle mind, ReadOnlySpan<Percept> percepts, PerceptionDefinition senses, ISubjectResolver resolver, ulong now, out FocusChoice focus);
}

/// <summary>
/// <see cref="IPerception"/> over the spatial index. Occlusion (line of sight) and scent arrive with the physics adapter (M1-20).
/// Preallocated query buffers; simulation thread only; no allocation (R21).
/// </summary>
public sealed class Perceiver : IPerception
{
    private const int QueryCapacity = 128;
    private const float CentimetresPerMetre = 100f;
    private const float DegreesToRadians = MathF.PI / 180f;
    // Above the highest non-player score (intensity 300 x 10 + distance 1000), so players always come first.
    private const int PlayerBonus = 5000;
    private const int IntensityWeight = 10;
    private const int MaxNormalised = 1000;

    private readonly ISpatialIndex _index;
    private readonly IOpinionModel _opinions;
    private readonly Entity[] _seen = new Entity[QueryCapacity];
    private readonly Entity[] _heard = new Entity[QueryCapacity];

    /// <summary>Creates the perceiver.</summary>
    public Perceiver(ISpatialIndex index, IOpinionModel opinions)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(opinions);

        _index = index;
        _opinions = opinions;
    }

    /// <inheritdoc />
    public int Perceive(Entity self, Vector3 position, Vector3 forward, PerceptionDefinition senses, Span<Percept> results)
    {
        ArgumentNullException.ThrowIfNull(senses);

        int seenCount = 0;
        bool hasDirection = new Vector2(forward.X, forward.Z).LengthSquared() > 0f;
        if (hasDirection)
        {
            seenCount = _index.QueryCone(position, forward, senses.SightRange, senses.SightHalfAngle * DegreesToRadians, _seen).Count;
        }

        int heardCount = _index.QueryRadius(position, senses.HearingRange, _heard).Count;
        int count = 0;
        for (int i = 0; i < seenCount; i++)
        {
            count = Insert(results, count, self, _seen[i], position, Sense.Sight);
        }

        for (int i = 0; i < heardCount; i++)
        {
            if (Array.IndexOf(_seen, _heard[i], 0, seenCount) < 0)
            {
                count = Insert(results, count, self, _heard[i], position, Sense.Hearing);
            }
        }

        return count;
    }

    /// <inheritdoc />
    public bool SelectFocus(MindHandle mind, ReadOnlySpan<Percept> percepts, PerceptionDefinition senses, ISubjectResolver resolver, ulong now, out FocusChoice focus)
    {
        ArgumentNullException.ThrowIfNull(senses);
        ArgumentNullException.ThrowIfNull(resolver);

        focus = default;
        int bestScore = -1;
        int range = Math.Max(senses.SightRange, senses.HearingRange) * (int)CentimetresPerMetre;
        foreach (Percept percept in percepts)
        {
            if (!resolver.TryResolve(percept.Entity, out MemorySubject subject))
            {
                continue;
            }

            int distance = Math.Min(MaxNormalised, (int)((long)percept.DistanceCm * MaxNormalised / range));
            int score = (subject.Kind == SubjectKind.Player ? PlayerBonus : 0)
                + (_opinions.GetOpinion(mind, subject, now).Intensity * IntensityWeight)
                + (MaxNormalised - distance);
            if (score > bestScore)
            {
                bestScore = score;
                focus = new FocusChoice(percept.Entity, subject, distance);
            }
        }

        return bestScore >= 0;
    }

    // Keeps results sorted nearest first (ties keep query order, deterministic). When full, a nearer entity replaces the farthest.
    private int Insert(Span<Percept> results, int count, Entity self, Entity other, Vector3 position, Sense sense)
    {
        if (other == self || results.Length == 0 || !_index.TryGetPosition(other, out Vector3 otherPosition))
        {
            return count;
        }

        Vector3 offset = otherPosition - position;
        int distance = (int)(new Vector2(offset.X, offset.Z).Length() * CentimetresPerMetre);
        if (count == results.Length)
        {
            if (results[count - 1].DistanceCm <= distance)
            {
                return count;
            }

            count--;
        }

        int at = count;
        while (at > 0 && results[at - 1].DistanceCm > distance)
        {
            results[at] = results[at - 1];
            at--;
        }

        results[at] = new Percept(other, distance, sense);
        return count + 1;
    }
}
