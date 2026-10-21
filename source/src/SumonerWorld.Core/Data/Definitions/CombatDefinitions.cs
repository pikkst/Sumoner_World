namespace SumonerWorld.Core.Data.Definitions;

/// <summary>Damage types (combat spec §2); more arrive with their magic schools.</summary>
public enum DamageType
{
    /// <summary>Blades, teeth, horns.</summary>
    Physical,

    /// <summary>The Primal school (nature).</summary>
    Primal,
}

/// <summary>Where a move can hit, around the attacker (combat spec §3).</summary>
public enum MoveShapeKind
{
    /// <summary>A slice of a circle in front of the attacker: <c>reachCm</c> and <c>angleDeg</c> (the whole opening).</summary>
    Arc,

    /// <summary>A strip straight ahead: <c>reachCm</c> long and <c>widthCm</c> wide.</summary>
    Line,

    /// <summary>All around the attacker: radius <c>reachCm</c>.</summary>
    Circle,
}

/// <summary>A move's hit area.</summary>
public sealed record MoveShapeDefinition
{
    /// <summary>The kind of area.</summary>
    public required MoveShapeKind Kind { get; init; }

    /// <summary>Reach from the attacker's centre to the far edge, in centimetres (target body radius is added).</summary>
    public required int ReachCm { get; init; }

    /// <summary>For <see cref="MoveShapeKind.Arc"/>: the whole opening angle in degrees.</summary>
    public int AngleDeg { get; init; }

    /// <summary>For <see cref="MoveShapeKind.Line"/>: the strip's width in centimetres.</summary>
    public int WidthCm { get; init; }
}

/// <summary>
/// A move, loaded from <c>data/combat/moves*.json</c> (kind <c>moves</c>): anything that deals damage, shared by players and
/// creatures (combat spec §3, Combat contract v1.0.0). Ticks are simulation ticks (30 per second).
/// </summary>
public sealed record MoveDefinition
{
    /// <summary>Stable id in snake_case, e.g. <c>light</c>.</summary>
    public required string Id { get; init; }

    /// <summary>Telegraph before the hit can land.</summary>
    public required int WindupTicks { get; init; }

    /// <summary>Ticks in which the hit can land.</summary>
    public required int ActiveTicks { get; init; }

    /// <summary>Ticks after the active part in which the attacker cannot act.</summary>
    public required int RecoveryTicks { get; init; }

    /// <summary>The hit area.</summary>
    public required MoveShapeDefinition Shape { get; init; }

    /// <summary>Base damage before power, resistance and guard.</summary>
    public required int Damage { get; init; }

    /// <summary>The damage type.</summary>
    public required DamageType DamageType { get; init; }

    /// <summary>Stamina paid when the move starts.</summary>
    public required int Stamina { get; init; }

    /// <summary>Mana paid when the move starts.</summary>
    public int Mana { get; init; }

    /// <summary>How hard the hit is to shrug off (stagger, combat spec §5; used from M2-02).</summary>
    public required int Poise { get; init; }

    /// <summary>Tags for combos and the AI (<c>heavy</c>, <c>charge</c>, <c>bite</c>, …).</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Whole length of the move in ticks.</summary>
    public int TotalTicks => WindupTicks + ActiveTicks + RecoveryTicks;
}

/// <summary>Resistance per damage type, per mille of the damage removed (0–750).</summary>
public sealed record ResistDefinition
{
    /// <summary>Against <see cref="DamageType.Physical"/>.</summary>
    public required int Physical { get; init; }

    /// <summary>Against <see cref="DamageType.Primal"/>.</summary>
    public required int Primal { get; init; }
}

/// <summary>A combatant's base stats (combat spec §2): a species' <c>combat</c> block, or the player's in the combat file.</summary>
public sealed record CombatStatsDefinition
{
    /// <summary>Health at full; 0 = defeated.</summary>
    public required int Health { get; init; }

    /// <summary>Stamina at full.</summary>
    public required int Stamina { get; init; }

    /// <summary>Mana at full (0 for creatures without magic).</summary>
    public required int Mana { get; init; }

    /// <summary>Outgoing damage in percent (100 = ×1.0).</summary>
    public required int Power { get; init; }

    /// <summary>Flat damage removed from every hit.</summary>
    public required int Guard { get; init; }

    /// <summary>Hits with less poise than this do not stagger (used from M2-02).</summary>
    public required int Poise { get; init; }

    /// <summary>Resistances.</summary>
    public required ResistDefinition Resist { get; init; }

    /// <summary>Ids of the moves this combatant can use (see <see cref="MoveDefinition"/>); the first is its basic attack.</summary>
    public required IReadOnlyList<string> Moves { get; init; }
}

/// <summary>
/// The combat rules and the player's base stats, loaded from <c>data/combat/combat.json</c> (kind <c>combat</c>, exactly one
/// file). Rates are per second and applied per tick without rounding drift.
/// </summary>
public sealed record CombatDefinition
{
    /// <summary>The player's base stats at the start (attributes and gear add to them later).</summary>
    public required CombatStatsDefinition Player { get; init; }

    /// <summary>Stamina regained per second while not spending.</summary>
    public required int StaminaRegenPerSecond { get; init; }

    /// <summary>Ticks after the last stamina spend before it regenerates.</summary>
    public required int StaminaRegenDelayTicks { get; init; }

    /// <summary>Mana regained per second.</summary>
    public required int ManaRegenPerSecond { get; init; }

    /// <summary>Health regained per second out of combat, per mille of full health.</summary>
    public required int HealthRegenPerMillePerSecond { get; init; }

    /// <summary>Ticks without taking damage before health regenerates.</summary>
    public required int HealthRegenDelayTicks { get; init; }

    /// <summary>A wild creature below this share of its health (per mille) may yield (combat spec §6).</summary>
    public required int YieldBelowPerMille { get; init; }

    /// <summary>Creatures bolder than this fight on instead of yielding (placeholder until the AI decides it, M2-03).</summary>
    public required int YieldMaxBoldness { get; init; }

    /// <summary>Ticks a yielded creature must go unharmed to count as spared.</summary>
    public required int SpareAfterTicks { get; init; }

    /// <summary>Ticks a defeated player stays down before getting up with full health.</summary>
    public required int PlayerDownTicks { get; init; }
}
