using SumonerWorld.Core.Cognition.Memory;

namespace SumonerWorld.Core.Cognition.Opinions;

/// <summary>
/// <see cref="IOpinionModel"/> over an <see cref="ICreatureMindStore"/>. Uses one preallocated recall buffer, so it does not
/// allocate (R21) and must be used from the simulation thread only.
/// </summary>
public sealed class OpinionModel : IOpinionModel
{
    private readonly ICreatureMindStore _store;
    private readonly MemoryRules _rules;
    private readonly RecalledMemory[] _recalled = new RecalledMemory[ICreatureMindStore.CoreCapacity + ICreatureMindStore.EpisodicCapacity];

    /// <summary>Creates the model.</summary>
    public OpinionModel(ICreatureMindStore store, MemoryRules rules)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(rules);

        _store = store;
        _rules = rules;
    }

    /// <inheritdoc />
    public Opinion GetOpinion(MindHandle mind, MemorySubject subject, ulong now)
    {
        int count = _store.RecallAbout(mind, subject, now, _recalled);
        long trust = 0;
        long fear = 0;
        long affection = 0;
        long respect = 0;
        for (int i = 0; i < count; i++)
        {
            Opinion weights = _rules.OpinionWeights(_recalled[i].Record.EventType);
            int strength = _recalled[i].EffectiveSalience;
            trust += (long)weights.Trust * strength;
            fear += (long)weights.Fear * strength;
            affection += (long)weights.Affection * strength;
            respect += (long)weights.Respect * strength;
        }

        Opinion baseline = _store.GetOpinionBaseline(mind, subject);
        return new Opinion(
            (int)(trust / ICreatureMindStore.MaxSalience) + baseline.Trust,
            (int)(fear / ICreatureMindStore.MaxSalience) + baseline.Fear,
            (int)(affection / ICreatureMindStore.MaxSalience) + baseline.Affection,
            (int)(respect / ICreatureMindStore.MaxSalience) + baseline.Respect).Clamped();
    }

    /// <inheritdoc />
    public bool Knows(MindHandle mind, MemorySubject subject, ulong now) =>
        _store.RecallAbout(mind, subject, now, _recalled.AsSpan(0, 1)) > 0
        || _store.GetOpinionBaseline(mind, subject) != Opinion.Neutral;
}
