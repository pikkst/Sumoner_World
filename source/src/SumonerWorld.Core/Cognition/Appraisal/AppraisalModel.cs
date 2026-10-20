using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Cognition.Opinions;

namespace SumonerWorld.Core.Cognition.Appraisal;

/// <summary>
/// <see cref="IAppraisal"/> (AI Bible §6). Emotions are stored per mind slot with the tick they were last set and decay lazily
/// toward a personality baseline, like memories (integer maths, R11). Preallocated, single simulation thread (R21).
/// </summary>
/// <remarks>
/// Rules, in priority order, for the emote shown toward a subject:
/// 1. <b>Grief</b> if a grief memory (BondedDied, OwnerDied) is younger than the grief window.
/// 2. A negative core memory about the subject: <b>Grudge</b> if the creature is bold (Boldness ≥ 0), otherwise <b>Fear</b>.
/// 3. <b>Fear</b> if fear is the dominant emotion and at least the emote threshold.
/// 4. <b>Affection</b> if Affection &gt; 60, else <b>Trust</b> if Trust &gt; 50.
/// 5. <b>Curiosity</b> if the subject is new to the creature and curiosity is dominant and at least the threshold.
/// 6. Otherwise no emote.
/// Hostility (negative trust) raises Anger in bold creatures and Fear in timid ones (AI Bible §6.1).
/// </remarks>
public sealed class AppraisalModel : IAppraisal
{
    private const int EmotionCount = 5;
    private const int TraitToBaselineDivisor = 4;

    private readonly ICreatureMindStore _store;
    private readonly IOpinionModel _opinions;
    private readonly MemoryRules _rules;
    private readonly AppraisalSettings _settings;
    private readonly ushort[] _griefEventTypes;
    private readonly int[] _values;
    private readonly uint[] _lastTicks;
    private readonly int[] _owners;
    private readonly RecalledMemory[] _recalled = new RecalledMemory[ICreatureMindStore.CoreCapacity + ICreatureMindStore.EpisodicCapacity];

    /// <summary>Creates the model for every mind slot of <paramref name="store"/>.</summary>
    public AppraisalModel(ICreatureMindStore store, IOpinionModel opinions, MemoryRules rules, AppraisalSettings settings)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(opinions);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(settings);

        _store = store;
        _opinions = opinions;
        _rules = rules;
        _settings = settings;
        _griefEventTypes = [.. settings.GriefEventKeys.Select(rules.FindByKey).OfType<ushort>()];
        _values = new int[store.Capacity * EmotionCount];
        _lastTicks = new uint[store.Capacity];
        _owners = new int[store.Capacity];
        Array.Fill(_owners, -1);
    }

    /// <inheritdoc />
    public EmotionState GetEmotions(MindHandle mind, ulong now)
    {
        Span<int> values = Refresh(mind, now);
        return new EmotionState(values[0], values[1], values[2], values[3], values[4]);
    }

    /// <inheritdoc />
    public EmotionState PeekEmotions(MindHandle mind, ulong now)
    {
        Span<int> values = stackalloc int[EmotionCount];
        Decayed(mind, now, values);
        return new EmotionState(values[0], values[1], values[2], values[3], values[4]);
    }

    /// <inheritdoc />
    public void Stimulate(MindHandle mind, Emotion emotion, int amount, ulong now)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        Span<int> values = Refresh(mind, now);
        values[(int)emotion] = Math.Min(EmotionState.Max, values[(int)emotion] + amount);
    }

    /// <inheritdoc />
    public Emote Appraise(MindHandle mind, MemorySubject subject, ulong now)
    {
        Span<int> values = Refresh(mind, now);
        Personality personality = _store.GetPersonality(mind);
        Opinion opinion = _opinions.GetOpinion(mind, subject, now);
        bool known = _opinions.Knows(mind, subject, now);

        int hostility = Math.Max(0, -opinion.Trust);
        bool bold = personality.Boldness >= 0;
        int fearTarget = Math.Max(opinion.Fear, bold ? 0 : hostility);
        Raise(values, Emotion.Fear, fearTarget);
        Raise(values, Emotion.Anger, bold ? hostility : 0);
        Raise(values, Emotion.Joy, opinion.Affection);
        Raise(values, Emotion.Curiosity, known ? 0 : (personality.Curiosity + Personality.MaxTrait) / 2);
        Raise(values, Emotion.Stress, Math.Max(0, fearTarget - Math.Max(0, opinion.Trust)) / 2);

        if (HasRecentGrief(mind, now))
        {
            return Emote.Grief;
        }

        if (HasNegativeCoreMemoryAbout(mind, subject, now))
        {
            return bold ? Emote.Grudge : Emote.Fear;
        }

        var emotions = new EmotionState(values[0], values[1], values[2], values[3], values[4]);
        Emotion dominant = emotions.Dominant;
        if (dominant == Emotion.Fear && emotions.Fear >= _settings.EmoteThreshold)
        {
            return Emote.Fear;
        }

        if (opinion.Affection > _settings.AffectionEmoteAbove)
        {
            return Emote.Affection;
        }

        if (opinion.Trust > _settings.TrustEmoteAbove)
        {
            return Emote.Trust;
        }

        if (!known && dominant == Emotion.Curiosity && emotions.Curiosity >= _settings.EmoteThreshold)
        {
            return Emote.Curiosity;
        }

        return Emote.None;
    }

    private static void Raise(Span<int> values, Emotion emotion, int target) =>
        values[(int)emotion] = Math.Clamp(Math.Max(values[(int)emotion], target), 0, EmotionState.Max);

    private static int Baseline(Personality personality, int emotion) => (Emotion)emotion switch
    {
        Emotion.Fear => Math.Max(0, -personality.Boldness) / TraitToBaselineDivisor,
        Emotion.Anger => Math.Max(0, (int)personality.Temper) / TraitToBaselineDivisor,
        Emotion.Curiosity => Math.Max(0, (int)personality.Curiosity) / TraitToBaselineDivisor,
        _ => 0,
    };

    // Brings the slot's stored emotions up to date (decay toward baseline) and returns them for in-place updates.
    private Span<int> Refresh(MindHandle mind, ulong now)
    {
        Span<int> values = _values.AsSpan(mind.Slot * EmotionCount, EmotionCount);
        Decayed(mind, now, values);
        _owners[mind.Slot] = mind.Generation;
        _lastTicks[mind.Slot] = (uint)now;
        return values;
    }

    // Writes the decayed emotions into values (which may be the stored slot itself) without touching the stored tick.
    private void Decayed(MindHandle mind, ulong now, Span<int> values)
    {
        Personality personality = _store.GetPersonality(mind);
        if (now > uint.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(now), now, "World tick exceeds the emotion tick range.");
        }

        uint tick = (uint)now;
        ReadOnlySpan<int> stored = _values.AsSpan(mind.Slot * EmotionCount, EmotionCount);
        bool fresh = _owners[mind.Slot] != mind.Generation;
        if (!fresh && tick < _lastTicks[mind.Slot])
        {
            throw new ArgumentOutOfRangeException(nameof(now), now, $"Time went backwards: emotions last set at tick {_lastTicks[mind.Slot]}.");
        }

        for (int i = 0; i < EmotionCount; i++)
        {
            int baseline = Baseline(personality, i);
            int excess = fresh ? 0 : stored[i] - baseline;
            values[i] = excess <= 0
                ? baseline
                : baseline + MemoryDecay.Apply(excess, tick - _lastTicks[mind.Slot], (uint)_settings.EmotionHalfLifeTicks);
        }
    }

    private bool HasRecentGrief(MindHandle mind, ulong now) =>
        HasRecentGrief(_store.GetCoreRecords(mind), now) || HasRecentGrief(_store.GetEpisodicRecords(mind), now);

    private bool HasRecentGrief(ReadOnlySpan<MemoryRecord> records, ulong now)
    {
        foreach (MemoryRecord record in records)
        {
            if (Array.IndexOf(_griefEventTypes, record.EventType) >= 0 && now - record.CreatedTick <= (ulong)_settings.GriefWindowTicks)
            {
                return true;
            }
        }

        return false;
    }

    private bool HasNegativeCoreMemoryAbout(MindHandle mind, MemorySubject subject, ulong now)
    {
        int count = _store.RecallAbout(mind, subject, now, _recalled);
        for (int i = 0; i < count; i++)
        {
            MemoryRecord record = _recalled[i].Record;
            if ((record.Tags & MemoryTags.Core) != 0 && record.Valence < 0 && _rules.Get(record.EventType).IsCore)
            {
                return true;
            }
        }

        return false;
    }
}
