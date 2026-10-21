using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Cognition.Opinions;
using SumonerWorld.Core.Data.Definitions;

namespace SumonerWorld.Core.Bonding;

/// <summary>A set of Pact Terms, one bit per catalogue entry (AI Bible §10.2). The bit order is part of the save format.</summary>
/// <param name="Mask">Bit <c>i</c> set = term <c>i</c> of the catalogue.</param>
public readonly record struct PactTerms(byte Mask)
{
    /// <summary>No terms.</summary>
    public static PactTerms None => default;

    /// <summary>Number of terms in the set.</summary>
    public int Count => System.Numerics.BitOperations.PopCount(Mask);

    /// <summary>A set with the given catalogue indices.</summary>
    public static PactTerms Of(params int[] indices)
    {
        ArgumentNullException.ThrowIfNull(indices);
        byte mask = 0;
        foreach (int index in indices)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, PactTermDefinition.MaxTerms);
            mask |= (byte)(1 << index);
        }

        return new PactTerms(mask);
    }

    /// <summary>True if term <paramref name="index"/> is in the set.</summary>
    public bool Contains(int index) => (uint)index < PactTermDefinition.MaxTerms && (Mask & (1 << index)) != 0;
}

/// <summary>
/// Why a creature accepted or refused a Pact: every part of the score, so the game can show the player why (Pillar 1).
/// <c>Score = Terms + Trust + Respect + Reputation − FearPenalty − GrudgePenalty + LevelGap</c>; accepted when Score ≥ Threshold,
/// and never when the creature belongs to another class.
/// </summary>
/// <param name="Accepted">The creature accepts.</param>
/// <param name="Score">Total acceptance score.</param>
/// <param name="Threshold">The species' Pact threshold.</param>
/// <param name="Terms">Value of the offered terms to this creature.</param>
/// <param name="Trust">3 × Trust in the player.</param>
/// <param name="Respect">3 × Respect for the player.</param>
/// <param name="Reputation">The player's reputation with the species in this region / 5.</param>
/// <param name="FearPenalty">3 × Fear of the player.</param>
/// <param name="GrudgePenalty">Penalty for a negative core memory about the player (e.g. it killed kin).</param>
/// <param name="LevelGap">The level difference's part (companions spec §2, M2-09).</param>
/// <param name="WrongClass">The creature belongs to another class: it always refuses (companions spec §2).</param>
public readonly record struct PactDecision(
    bool Accepted, int Score, int Threshold, int Terms, int Trust, int Respect, int Reputation, int FearPenalty, int GrudgePenalty, int LevelGap = 0, bool WrongClass = false);

/// <summary>Decides whether a creature accepts a Pact (AI Bible §10.2). Deterministic: no random roll decides it (R15).</summary>
public interface IPactEvaluator
{
    /// <summary>
    /// Evaluates an offer of <paramref name="terms"/> from <paramref name="player"/> with server reputation
    /// <paramref name="reputation"/> (−1000…1000, AI Bible §4.5); <paramref name="conditions"/> add the class rule and the level
    /// difference (companions spec §2).
    /// </summary>
    PactDecision Evaluate(
        MindHandle creature, SpeciesDefinition species, MemorySubject player, PactTerms terms, int reputation, ulong now, Progression.PactConditions? conditions = null);

    /// <summary>The value of one catalogue term to this creature right now.</summary>
    int TermValue(MindHandle creature, int termIndex, ulong now);
}

/// <summary><see cref="IPactEvaluator"/> with the AI Bible §10.2 formula (starting weights, tuned in the M1 playtest).</summary>
public sealed class PactEvaluator : IPactEvaluator
{
    /// <summary>Weight of Trust and Respect in the score.</summary>
    public const int OpinionWeight = 3;

    /// <summary>Weight of Fear in the penalty.</summary>
    public const int FearWeight = 3;

    /// <summary>Reputation divisor.</summary>
    public const int ReputationDivisor = 5;

    /// <summary>Penalty for a grudge (negative core memory about the player).</summary>
    public const int GrudgePenalty = 300;

    private const int Percent = 100;
    private const int TraitDivisor = 10;
    private const int MaxReputation = 1000;

    private readonly ICreatureMindStore _store;
    private readonly IOpinionModel _opinions;
    private readonly IReadOnlyList<PactTermDefinition> _terms;
    private readonly RecalledMemory[] _recalled = new RecalledMemory[ICreatureMindStore.CoreCapacity + ICreatureMindStore.EpisodicCapacity];

    /// <summary>Creates the evaluator over the term catalogue (index = bit).</summary>
    public PactEvaluator(ICreatureMindStore store, IOpinionModel opinions, IReadOnlyList<PactTermDefinition> terms)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(opinions);
        ArgumentNullException.ThrowIfNull(terms);

        _store = store;
        _opinions = opinions;
        _terms = terms;
    }

    /// <inheritdoc />
    public PactDecision Evaluate(
        MindHandle creature, SpeciesDefinition species, MemorySubject player, PactTerms terms, int reputation, ulong now, Progression.PactConditions? conditions = null)
    {
        ArgumentNullException.ThrowIfNull(species);
        ValidateTerms(terms);

        int termValue = 0;
        for (int i = 0; i < _terms.Count; i++)
        {
            termValue += terms.Contains(i) ? TermValue(creature, i, now) : 0;
        }

        Opinion opinion = _opinions.GetOpinion(creature, player, now);
        int trust = OpinionWeight * opinion.Trust;
        int respect = OpinionWeight * opinion.Respect;
        int reputationPart = Math.Clamp(reputation, -MaxReputation, MaxReputation) / ReputationDivisor;
        int fearPenalty = FearWeight * Math.Max(0, opinion.Fear);
        int grudgePenalty = HasGrudge(creature, player, now) ? GrudgePenalty : 0;
        int levelGap = conditions?.LevelBonus ?? 0;
        bool wrongClass = conditions?.WrongClass ?? false;
        int score = termValue + trust + respect + reputationPart - fearPenalty - grudgePenalty + levelGap;

        return new PactDecision(
            !wrongClass && score >= species.PactThreshold, score, species.PactThreshold, termValue, trust, respect, reputationPart, fearPenalty, grudgePenalty, levelGap, wrongClass);
    }

    /// <inheritdoc />
    public int TermValue(MindHandle creature, int termIndex, ulong now)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(termIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(termIndex, _terms.Count);

        PactTermDefinition term = _terms[termIndex];
        int value = term.Base;
        if (term.Need is Need need)
        {
            value += _store.GetNeed(creature, need, now) * term.NeedWeight / Percent;
        }

        Cognition.Personality personality = _store.GetPersonality(creature);
        for (int i = 0; i < term.Personality.Count; i++)
        {
            value += term.Personality[i].Factor * personality.Get(term.Personality[i].Trait) / TraitDivisor;
        }

        return value;
    }

    private void ValidateTerms(PactTerms terms)
    {
        if (terms.Count is 0 or > PactTermDefinition.MaxTermsPerOffer || terms.Mask >> _terms.Count != 0)
        {
            throw new ArgumentException(
                $"A Pact offers 1–{PactTermDefinition.MaxTermsPerOffer} terms from the catalogue of {_terms.Count}; got mask {terms.Mask:b8}.",
                nameof(terms));
        }
    }

    private bool HasGrudge(MindHandle creature, MemorySubject player, ulong now)
    {
        int count = _store.RecallAbout(creature, player, now, _recalled);
        for (int i = 0; i < count; i++)
        {
            if ((_recalled[i].Record.Tags & MemoryTags.Core) != 0 && _recalled[i].Record.Valence < 0)
            {
                return true;
            }
        }

        return false;
    }
}
