namespace SumonerWorld.Core.Data.Definitions;

/// <summary>What a memory can be about (AI Bible §4.1 <c>Subject</c>).</summary>
public enum SubjectKind : byte
{
    /// <summary>A player account.</summary>
    Player,

    /// <summary>An individual creature.</summary>
    Creature,

    /// <summary>A whole species.</summary>
    Species,

    /// <summary>A place (region cell).</summary>
    Place,

    /// <summary>An item type.</summary>
    Item,
}

/// <summary>The four personality axes (AI Bible §3).</summary>
public enum PersonalityTrait
{
    /// <summary>Timid (−100) to bold (+100).</summary>
    Boldness,

    /// <summary>Independent (−100) to loyal (+100).</summary>
    Loyalty,

    /// <summary>Wary (−100) to curious (+100).</summary>
    Curiosity,

    /// <summary>Gentle (−100) to aggressive (+100).</summary>
    Temper,
}

/// <summary>How a core memory shifts personality when it forms (AI Bible §3: 1–5 points, capped at ±25 from birth).</summary>
public sealed record TraitDrift
{
    /// <summary>Largest allowed shift per core memory.</summary>
    public const int MaxAmount = 5;

    /// <summary>The trait that shifts.</summary>
    public required PersonalityTrait Trait { get; init; }

    /// <summary>Signed shift in trait points, 1–5 in either direction.</summary>
    public required int Amount { get; init; }
}

/// <summary>One row of the memory event table (AI Bible §4.2), loaded from <c>data/ai/memory_event_types.json</c>.</summary>
public sealed record MemoryEventTypeDefinition
{
    /// <summary>Lowest allowed valence.</summary>
    public const int MinValence = -100;

    /// <summary>Highest allowed valence.</summary>
    public const int MaxValence = 100;

    /// <summary>Highest allowed salience.</summary>
    public const int MaxSalience = 1000;

    /// <summary>Saved id. Never reused or renumbered (R26).</summary>
    public required ushort Id { get; init; }

    /// <summary>Stable name used in logs and tools, e.g. <c>FedBy</c>.</summary>
    public required string Key { get; init; }

    /// <summary>What the memory can be about.</summary>
    public required IReadOnlyList<SubjectKind> Subjects { get; init; }

    /// <summary>How good (+) or bad (−) the event was for the creature, in [−100, 100].</summary>
    public required int Valence { get; init; }

    /// <summary>Starting salience, in [1, 1000].</summary>
    public required int BaseSalience { get; init; }

    /// <summary>Half-life in in-game days; <c>null</c> means the memory never decays.</summary>
    public required int? HalfLifeDays { get; init; }

    /// <summary>True for core memories (pinned, shape personality; AI Bible §4.4).</summary>
    public required bool IsCore { get; init; }

    /// <summary>Personality shift applied once when this core memory forms; only allowed on core types.</summary>
    public TraitDrift? TraitDrift { get; init; }

    /// <summary>Largest allowed reputation change per event.</summary>
    public const int MaxReputationDelta = 500;

    /// <summary>
    /// Change to the player's server reputation with the creature's species in that region when this event happens to a creature
    /// (AI Bible §4.5); 0 = no effect.
    /// </summary>
    public int ReputationDelta { get; init; }
}
