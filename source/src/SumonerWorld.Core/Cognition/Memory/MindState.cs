using SumonerWorld.Core.Cognition.Opinions;

namespace SumonerWorld.Core.Cognition.Memory;

/// <summary>A long-term opinion baseline, in tenths of a point (AI Bible §5).</summary>
/// <param name="Subject">Who the opinion is about.</param>
/// <param name="Tenths">The baseline, in tenths.</param>
public readonly record struct OpinionBaseline(MemorySubject Subject, Opinion Tenths);

/// <summary>One need's saved state: level in thousandths of a point at <paramref name="Tick"/>, and its gain per day.</summary>
/// <param name="Milli">Level × 1000 at <paramref name="Tick"/>.</param>
/// <param name="Tick">When the level was last set.</param>
/// <param name="Rate">Gain per day.</param>
public readonly record struct NeedState(int Milli, uint Tick, int Rate);

/// <summary>
/// Everything saved about one creature's mind ("CreatureMind v1", AI Bible §4.6): personality at birth and now, core and episodic
/// memories, opinion baselines and needs. Exported and imported by <see cref="CreatureMindStore"/>; the slot it lives in is not part
/// of it.
/// </summary>
/// <param name="Birth">Personality at birth.</param>
/// <param name="Current">Personality after drift.</param>
/// <param name="CoreRecords">Core memories, in slot order.</param>
/// <param name="EpisodicRecords">Episodic memories, in slot order.</param>
/// <param name="Baselines">Opinion baselines, in slot order.</param>
/// <param name="Needs">The five needs, in <see cref="Need"/> order.</param>
public sealed record MindState(
    Personality Birth,
    Personality Current,
    IReadOnlyList<MemoryRecord> CoreRecords,
    IReadOnlyList<MemoryRecord> EpisodicRecords,
    IReadOnlyList<OpinionBaseline> Baselines,
    IReadOnlyList<NeedState> Needs);
