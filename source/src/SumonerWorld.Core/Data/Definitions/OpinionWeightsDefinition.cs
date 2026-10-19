namespace SumonerWorld.Core.Data.Definitions;

/// <summary>
/// How much a memory of one event type moves each attitude (AI Bible §5), loaded from <c>data/ai/opinion_weights.json</c>.
/// A full-strength memory (salience 1000) with weight 100 contributes 100 attitude points; event types without an entry
/// do not affect opinions.
/// </summary>
public sealed record OpinionWeightsDefinition
{
    /// <summary>Lowest allowed weight.</summary>
    public const int MinWeight = -100;

    /// <summary>Highest allowed weight.</summary>
    public const int MaxWeight = 100;

    /// <summary>Memory event type id this row applies to.</summary>
    public required ushort EventType { get; init; }

    /// <summary>Weight on Trust, in [−100, 100].</summary>
    public required int Trust { get; init; }

    /// <summary>Weight on Fear, in [−100, 100].</summary>
    public required int Fear { get; init; }

    /// <summary>Weight on Affection, in [−100, 100].</summary>
    public required int Affection { get; init; }

    /// <summary>Weight on Respect, in [−100, 100].</summary>
    public required int Respect { get; init; }
}
