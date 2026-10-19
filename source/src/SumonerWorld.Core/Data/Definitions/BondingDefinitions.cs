using SumonerWorld.Core.Cognition.Memory;

namespace SumonerWorld.Core.Data.Definitions;

/// <summary>
/// One Pact Term a player can offer (AI Bible §10.2), loaded from <c>data/ai/pact_terms.json</c>. Its value to a creature is
/// <c>base + need × needWeight / 100 + Σ factor × trait / 10</c> (personality factors in −10…10 give at most ±100 each).
/// </summary>
public sealed record PactTermDefinition
{
    /// <summary>Most terms in the catalogue (they are stored as a bitmask).</summary>
    public const int MaxTerms = 8;

    /// <summary>Most terms in one offer (AI Bible §10.2).</summary>
    public const int MaxTermsPerOffer = 3;

    /// <summary>Stable name, e.g. <c>FeedDaily</c>; its position in the file is its bit and must never change (saves).</summary>
    public required string Key { get; init; }

    /// <summary>Value to every creature, in acceptance points (0–300).</summary>
    public required int Base { get; init; }

    /// <summary>A need that makes the term more attractive the higher it is, if any.</summary>
    public Need? Need { get; init; }

    /// <summary>Percent of the need level added (0–300).</summary>
    public int NeedWeight { get; init; }

    /// <summary>How personality changes the value.</summary>
    public required IReadOnlyList<PersonalityFactor> Personality { get; init; }
}

/// <summary>One bond level (AI Bible §10.3), loaded from <c>data/ai/bond_levels.json</c>.</summary>
public sealed record BondLevelDefinition
{
    /// <summary>Level number; levels are 0, 1, 2… with no gaps.</summary>
    public required int Level { get; init; }

    /// <summary>Stable name, e.g. <c>Companion</c>.</summary>
    public required string Key { get; init; }

    /// <summary>Trust toward the owner needed.</summary>
    public required int MinTrust { get; init; }

    /// <summary>Affection toward the owner needed.</summary>
    public required int MinAffection { get; init; }

    /// <summary>Core memories about the owner needed.</summary>
    public required int CoreMemoriesWithOwner { get; init; }

    /// <summary>In-game days since the Pact needed.</summary>
    public required int MinDaysBonded { get; init; }
}
