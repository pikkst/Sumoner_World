namespace SumonerWorld.Core.Data.Definitions;

/// <summary>What a memory can be about (AI Bible §4.1 <c>Subject</c>).</summary>
public enum SubjectKind
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
}
