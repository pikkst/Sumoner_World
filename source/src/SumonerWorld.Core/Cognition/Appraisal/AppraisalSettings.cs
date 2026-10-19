namespace SumonerWorld.Core.Cognition.Appraisal;

/// <summary>
/// Tuning values for emotions and emote selection (AI Bible §6). Defaults are starting values to be tuned in the M1 playtest;
/// they are configuration, not constants in the rules (R48).
/// </summary>
public sealed class AppraisalSettings
{
    /// <summary>Emotions fall halfway back to their baseline in this many ticks (10 s at 30 Hz).</summary>
    public int EmotionHalfLifeTicks { get; init; } = 300;

    /// <summary>Fear or curiosity must be at least this strong (and dominant) to show its emote.</summary>
    public int EmoteThreshold { get; init; } = 40;

    /// <summary>Trust above this shows the Trust emote (AI Bible §6.2: Trust &gt; 50).</summary>
    public int TrustEmoteAbove { get; init; } = 50;

    /// <summary>Affection above this shows the Affection emote (AI Bible §6.2: Affection &gt; 60).</summary>
    public int AffectionEmoteAbove { get; init; } = 60;

    /// <summary>A death memory younger than this triggers Grief (1 in-game day at 30 Hz).</summary>
    public int GriefWindowTicks { get; init; } = 86_400;

    /// <summary>Memory event keys that cause grief (AI Bible §6.2).</summary>
    public IReadOnlyList<string> GriefEventKeys { get; init; } = ["BondedDied", "OwnerDied"];

    /// <summary>The default settings.</summary>
    public static AppraisalSettings Default { get; } = new();
}
