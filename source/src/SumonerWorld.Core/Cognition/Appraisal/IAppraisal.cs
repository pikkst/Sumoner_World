using SumonerWorld.Core.Cognition.Memory;

namespace SumonerWorld.Core.Cognition.Appraisal;

/// <summary>
/// Short-term emotions and emote selection (AI Bible §6). Emotions rise from stimuli and from appraising a subject, and fall back
/// to a personality baseline over seconds. Emotions are not saved.
/// </summary>
public interface IAppraisal
{
    /// <summary>The creature's emotions at tick <paramref name="now"/>.</summary>
    EmotionState GetEmotions(MindHandle mind, ulong now);

    /// <summary>
    /// The same values as <see cref="GetEmotions"/> without storing the decay (debug tools such as the AI Inspector: decay rounds,
    /// so an extra stored refresh would change later values and the simulation would no longer be deterministic).
    /// </summary>
    EmotionState PeekEmotions(MindHandle mind, ulong now);

    /// <summary>Raises one emotion by <paramref name="amount"/> (being hit, a loud noise, food appearing...).</summary>
    void Stimulate(MindHandle mind, Emotion emotion, int amount, ulong now);

    /// <summary>
    /// The creature looks at <paramref name="subject"/>: its opinion and whether the subject is new to it raise the matching
    /// emotions, and the emote it should show toward that subject is returned (AI Bible §6.2).
    /// </summary>
    Emote Appraise(MindHandle mind, MemorySubject subject, ulong now);
}
