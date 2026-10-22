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

    /// <summary>A circle of <c>radiusCm</c> around a point the caster chose up to <c>reachCm</c> ahead (Burst spells, M2-11).</summary>
    Burst,

    /// <summary>No area: it takes effect on the caster or the ally it names within <c>reachCm</c> (Ward spells, M2-11).</summary>
    Ward,
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

    /// <summary>For <see cref="MoveShapeKind.Burst"/>: the circle's radius in centimetres.</summary>
    public int RadiusCm { get; init; }
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

    /// <summary>A status effect it puts on what it hits (spells, M2-11), if any.</summary>
    public string? Effect { get; init; }

    /// <summary>Health it heals over its effect's time (ward spells).</summary>
    public int Heal { get; init; }

    /// <summary>The magic school of a spell (resonance), empty for other moves.</summary>
    public string School { get; init; } = string.Empty;

    /// <summary>True for spells (moves built from runes); everyone with a spell book may cast them.</summary>
    public bool IsSpell => School.Length > 0;

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

    /// <summary>Share of a blocked hit's damage that still gets through, per mille (combat spec §5: 300).</summary>
    public required int BlockDamagePerMille { get; init; }

    /// <summary>Whole angle in front of the blocker that a block covers, in degrees.</summary>
    public required int BlockArcDeg { get; init; }

    /// <summary>A block started at most this many ticks before the hit is a parry.</summary>
    public required int ParryWindowTicks { get; init; }

    /// <summary>Ticks of a dodge in which no hit lands.</summary>
    public required int DodgeTicks { get; init; }

    /// <summary>Stamina a dodge costs.</summary>
    public required int DodgeStamina { get; init; }

    /// <summary>Ticks from one dodge's start before the next may start.</summary>
    public required int DodgeCooldownTicks { get; init; }

    /// <summary>Movement speed during a dodge, in percent of walking speed.</summary>
    public required int DodgeSpeedPercent { get; init; }

    /// <summary>Ticks a stagger (poise break, parry, guard break) lasts.</summary>
    public required int StaggerTicks { get; init; }

    /// <summary>Mana a summon costs (AI Bible §10.4, M2-06).</summary>
    public required int SummonManaCost { get; init; }

    /// <summary>Ticks the summoning sigil takes before the creature arrives.</summary>
    public required int SummonCastTicks { get; init; }

    /// <summary>
    /// Active summons a player may have, by Summoner Rank F, E, D, … God (AI Bible §10.4, Academy spec §3.3, M2-20); a rank past
    /// the end of the list has the last value.
    /// </summary>
    public required IReadOnlyList<int> BondCapacityByRank { get; init; }

    /// <summary>The Bond Capacity of a rank (0 = F).</summary>
    public int BondCapacityOf(int rank) =>
        BondCapacityByRank.Count == 0 ? 1 : BondCapacityByRank[Math.Clamp(rank, 0, BondCapacityByRank.Count - 1)];

    /// <summary>How creatures grow (companions spec §3–4, M2-09).</summary>
    public GrowthDefinition Growth { get; init; } = new();
}

/// <summary>
/// Growth rules (companions spec §3–4): the level curve, experience per role, stats per level and the life-stage conditions.
/// Defaults are the spec's starting values; <c>combat.json</c> sets them.
/// </summary>
public sealed record GrowthDefinition
{
    /// <summary>Level <c>n</c> needs <c>xpFactor × (n − 1)^(xpExponentPercent / 100)</c> total experience.</summary>
    public int XpFactor { get; init; } = 40;

    /// <summary>The curve's exponent in percent (150 = 1.5).</summary>
    public int XpExponentPercent { get; init; } = 150;

    /// <summary>Damage dealt or taken at the owner's side per point of combat experience.</summary>
    public int DamagePerCombatXp { get; init; } = 2;

    /// <summary>Combat experience for a defeated wild creature.</summary>
    public int DefeatXp { get; init; } = 20;

    /// <summary>Metres walked while summoned per point of travel experience.</summary>
    public int MetresPerTravelXp { get; init; } = 50;

    /// <summary>Work experience per finished task.</summary>
    public int WorkTaskXp { get; init; } = 5;

    /// <summary>Health added per level above 1, in percent of the species' base.</summary>
    public int HealthPercentPerLevel { get; init; } = 6;

    /// <summary>Power added per level above 1, in percent of the species' base.</summary>
    public int PowerPercentPerLevel { get; init; } = 4;

    /// <summary>A companion may grow up at this share of its tier's cap, in percent …</summary>
    public int GrownAtCapPercent { get; init; } = 40;

    /// <summary>… with at least this bond level (Companion).</summary>
    public int GrownBondLevel { get; init; } = 2;

    /// <summary>Health a grown creature has over a young one, in percent.</summary>
    public int GrownHealthPercent { get; init; } = 20;

    /// <summary>A companion may become an elder at this share of its tier's cap, in percent …</summary>
    public int ElderAtCapPercent { get; init; } = 80;

    /// <summary>… with at least this bond level (Devoted).</summary>
    public int ElderBondLevel { get; init; } = 4;

    /// <summary>Power an elder has over a grown creature, in percent.</summary>
    public int ElderPowerPercent { get; init; } = 20;

    /// <summary>How long the summoner must rest (stand still, no fight) before a companion grows up, in ticks.</summary>
    public int RestTicks { get; init; } = 150;

    /// <summary>Player experience for defeating a creature (GDD §5, M2-09).</summary>
    public int PlayerDefeatXp { get; init; } = 20;

    /// <summary>Player experience for sparing a creature that yielded (sparing is worth more than killing, GDD §6).</summary>
    public int PlayerSpareXp { get; init; } = 30;

    /// <summary>Player experience for a sealed Pact.</summary>
    public int PlayerPactXp { get; init; } = 50;
}
