namespace SumonerWorld.Core.Data.Definitions;

/// <summary>
/// A combo (combat spec §10, GDD §3), loaded from <c>data/combat/combos.json</c>: a move with <see cref="MoveTag"/> hitting a
/// target that carries <see cref="Effect"/> does more damage and staggers it longer. A companion that lands it
/// <see cref="LearnAfter"/> times learns it and from then on starts it by itself when its summoner casts
/// <see cref="TriggerSpell"/>. A combo's position in the file is its saved bit and must never change.
/// </summary>
public sealed record ComboDefinition
{
    /// <summary>Most combos (saved as bits and counters).</summary>
    public const int MaxCombos = 4;

    /// <summary>Stable id, e.g. <c>uproot</c>.</summary>
    public required string Id { get; init; }

    /// <summary>The status effect the target must carry, e.g. <c>rooted</c>.</summary>
    public required string Effect { get; init; }

    /// <summary>The tag the hitting move must have, e.g. <c>charge</c>.</summary>
    public required string MoveTag { get; init; }

    /// <summary>Damage in percent of the move's normal damage.</summary>
    public required int DamagePercent { get; init; }

    /// <summary>Ticks the target is staggered (knocked down).</summary>
    public required int StaggerTicks { get; init; }

    /// <summary>Successful landings before a companion knows the combo (AI Bible §8).</summary>
    public required int LearnAfter { get; init; }

    /// <summary>The summoner's spell after which a companion that knows the combo starts it by itself.</summary>
    public required string TriggerSpell { get; init; }
}
