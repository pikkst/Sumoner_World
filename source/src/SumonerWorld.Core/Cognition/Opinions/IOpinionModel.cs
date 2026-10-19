using SumonerWorld.Core.Cognition.Memory;

namespace SumonerWorld.Core.Cognition.Opinions;

/// <summary>
/// Computes a creature's opinion of a subject from what it remembers (AI Bible §5): the sum of each remembered memory's
/// effective salience × its event type's opinion weights, plus the long-term baseline, clamped to [−100, 100].
/// Recomputed on every read, so opinions fade as memories fade.
/// </summary>
public interface IOpinionModel
{
    /// <summary>The creature's attitudes toward <paramref name="subject"/> at tick <paramref name="now"/>.</summary>
    Opinion GetOpinion(MindHandle mind, MemorySubject subject, ulong now);

    /// <summary>True if the creature remembers anything about the subject or keeps a baseline for it.</summary>
    bool Knows(MindHandle mind, MemorySubject subject, ulong now);
}
