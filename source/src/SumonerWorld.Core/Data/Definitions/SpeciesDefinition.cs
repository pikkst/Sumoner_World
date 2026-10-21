namespace SumonerWorld.Core.Data.Definitions;

/// <summary>Ecological role (GDD §7, style guide §3.3).</summary>
public enum CreatureRole
{
    /// <summary>Hunts other creatures.</summary>
    Predator,

    /// <summary>Eats plants, usually in herds.</summary>
    Grazer,

    /// <summary>Feeds on what others leave.</summary>
    Scavenger,

    /// <summary>Spirit creature.</summary>
    Spirit,

    /// <summary>Region boss / apex creature.</summary>
    Apex,
}

/// <summary>Shared rig family (DEC-002, style guide §3.4).</summary>
public enum BodyType
{
    /// <summary>Four legs.</summary>
    Quadruped,

    /// <summary>Winged.</summary>
    Avian,

    /// <summary>Legless, long body.</summary>
    Serpentine,

    /// <summary>Two legs.</summary>
    Biped,
}

/// <summary>Social structure used for group memory and rumours (AI Bible §4.5).</summary>
public enum GroupType
{
    /// <summary>Lives alone; does not share rumours.</summary>
    Solitary,

    /// <summary>Small hunting group.</summary>
    Pack,

    /// <summary>Large grazing group.</summary>
    Herd,

    /// <summary>Flying group.</summary>
    Flock,
}

/// <summary>Birth distribution of one personality trait (AI Bible §3): mean ± spread, both in trait points.</summary>
public sealed record TraitRange
{
    /// <summary>Species average, in [−100, 100].</summary>
    public required int Mean { get; init; }

    /// <summary>Maximum distance from the mean at birth, in [0, 100].</summary>
    public required int Spread { get; init; }
}

/// <summary>How fast each need rises, in need points (0–100) per in-game day (AI Bible §6.3).</summary>
public sealed record NeedRates
{
    /// <summary>Hunger gain per day.</summary>
    public required int Hunger { get; init; }

    /// <summary>Tiredness gain per day.</summary>
    public required int Rest { get; init; }

    /// <summary>Safety need gain per day.</summary>
    public required int Safety { get; init; }

    /// <summary>Social need gain per day.</summary>
    public required int Social { get; init; }

    /// <summary>Territory need gain per day.</summary>
    public required int Territory { get; init; }
}

/// <summary>A species' senses (AI Bible §2), in metres and degrees.</summary>
public sealed record PerceptionDefinition
{
    /// <summary>Longest allowed sense range, in metres.</summary>
    public const int MaxRange = 200;

    /// <summary>How far the creature sees inside its view cone.</summary>
    public required int SightRange { get; init; }

    /// <summary>Half the view cone, from straight ahead to the edge, in degrees (180 = all around).</summary>
    public required int SightHalfAngle { get; init; }

    /// <summary>How far the creature hears, in any direction.</summary>
    public required int HearingRange { get; init; }
}

/// <summary>A creature species, loaded from <c>data/ai/species/*.json</c> (AI Bible §12).</summary>
public sealed record SpeciesDefinition
{
    /// <summary>Stable id in snake_case, e.g. <c>mossback</c>. Never reused (R26).</summary>
    public required string Id { get; init; }

    /// <summary>Localization key for the display name (R39).</summary>
    public required string NameKey { get; init; }

    /// <summary>Ecological role.</summary>
    public required CreatureRole Role { get; init; }

    /// <summary>Rig family.</summary>
    public required BodyType BodyType { get; init; }

    /// <summary>Social structure.</summary>
    public required GroupType Group { get; init; }

    /// <summary>Boldness at birth (timid ↔ bold).</summary>
    public required TraitRange Boldness { get; init; }

    /// <summary>Loyalty at birth (independent ↔ loyal).</summary>
    public required TraitRange Loyalty { get; init; }

    /// <summary>Curiosity at birth (wary ↔ curious).</summary>
    public required TraitRange Curiosity { get; init; }

    /// <summary>Temper at birth (gentle ↔ aggressive).</summary>
    public required TraitRange Temper { get; init; }

    /// <summary>Need growth rates.</summary>
    public required NeedRates Needs { get; init; }

    /// <summary>
    /// Body footprint radius in centimetres: creatures keep this far apart (plus the other's radius) and stop this far from what
    /// they approach.
    /// </summary>
    public required int BodyRadiusCm { get; init; }

    /// <summary>Pact acceptance threshold (AI Bible §10.2), in [0, 1000].</summary>
    public required int PactThreshold { get; init; }

    /// <summary>The species' senses.</summary>
    public required PerceptionDefinition Perception { get; init; }

    /// <summary>Combat stats and moves (combat spec §2).</summary>
    public required CombatStatsDefinition Combat { get; init; }

    /// <summary>Keys of the goals this species can choose (AI Bible §7.1: species enable a subset).</summary>
    public required IReadOnlyList<string> Goals { get; init; }
}
