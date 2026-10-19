namespace SumonerWorld.Core.Cognition.Appraisal;

/// <summary>Short-term emotions (AI Bible §6.1), each 0–100. Not saved: they rebuild within seconds.</summary>
public enum Emotion
{
    /// <summary>Afraid.</summary>
    Fear,

    /// <summary>Angry.</summary>
    Anger,

    /// <summary>Happy.</summary>
    Joy,

    /// <summary>Interested in something new.</summary>
    Curiosity,

    /// <summary>Under pressure.</summary>
    Stress,
}

/// <summary>The visible emotes every body type can perform (AI Bible §6.2). <see cref="None"/> means no emote.</summary>
public enum Emote : byte
{
    /// <summary>No emote; normal behaviour.</summary>
    None,

    /// <summary>Relaxed, approaches, turns its back safely.</summary>
    Trust,

    /// <summary>Low posture, keeps distance, watches.</summary>
    Fear,

    /// <summary>Stares, blocks the path, growls, never turns its back.</summary>
    Grudge,

    /// <summary>Head tilt, circling, sniffing.</summary>
    Curiosity,

    /// <summary>Nuzzles, stays close, mirrors movement.</summary>
    Affection,

    /// <summary>Slow movement, returns to the place, calls.</summary>
    Grief,
}

/// <summary>A creature's five emotions at one moment, each in [0, 100].</summary>
/// <param name="Fear">Fear level.</param>
/// <param name="Anger">Anger level.</param>
/// <param name="Joy">Joy level.</param>
/// <param name="Curiosity">Curiosity level.</param>
/// <param name="Stress">Stress level.</param>
public readonly record struct EmotionState(int Fear, int Anger, int Joy, int Curiosity, int Stress)
{
    /// <summary>Highest emotion level.</summary>
    public const int Max = 100;

    /// <summary>The value of one emotion.</summary>
    public int Get(Emotion emotion) => emotion switch
    {
        Emotion.Fear => Fear,
        Emotion.Anger => Anger,
        Emotion.Joy => Joy,
        Emotion.Curiosity => Curiosity,
        Emotion.Stress => Stress,
        _ => throw new ArgumentOutOfRangeException(nameof(emotion), emotion, "Unknown emotion."),
    };

    /// <summary>The strongest emotion; ties go to the first in <see cref="Emotion"/> order (deterministic).</summary>
    public Emotion Dominant
    {
        get
        {
            Emotion dominant = Emotion.Fear;
            for (Emotion emotion = Emotion.Anger; emotion <= Emotion.Stress; emotion++)
            {
                if (Get(emotion) > Get(dominant))
                {
                    dominant = emotion;
                }
            }

            return dominant;
        }
    }
}
