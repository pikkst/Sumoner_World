namespace SumonerWorld.Core.Data.Definitions;

/// <summary>The two rune slots of a spell (combat spec §8).</summary>
public enum RuneSlot
{
    /// <summary>How the spell travels: bolt, burst or ward.</summary>
    Form,

    /// <summary>What it does: damage, an effect, healing.</summary>
    Essence,
}

/// <summary>What a form rune makes of a spell.</summary>
public enum SpellForm
{
    /// <summary>A projectile straight ahead (a line <c>rangeCm</c> long and <c>widthCm</c> wide).</summary>
    Bolt,

    /// <summary>A circle of <c>radiusCm</c> around a point up to <c>rangeCm</c> ahead.</summary>
    Burst,

    /// <summary>On the caster or an ally within <c>rangeCm</c>.</summary>
    Ward,
}

/// <summary>
/// A rune (combat spec §8, GDD §4: spells are built from data-driven runes), loaded from <c>data/magic/runes.json</c>. A form
/// rune sets the shape, an essence rune what the spell does.
/// </summary>
public sealed record RuneDefinition
{
    /// <summary>Stable id, e.g. <c>bolt</c>, <c>thorns</c>.</summary>
    public required string Id { get; init; }

    /// <summary>Form or essence.</summary>
    public required RuneSlot Slot { get; init; }

    /// <summary>Form runes: the shape.</summary>
    public SpellForm Form { get; init; }

    /// <summary>Form runes: how far it reaches, in centimetres.</summary>
    public int RangeCm { get; init; }

    /// <summary>Bolt: width in centimetres.</summary>
    public int WidthCm { get; init; }

    /// <summary>Burst: radius in centimetres.</summary>
    public int RadiusCm { get; init; }

    /// <summary>Essence runes: damage (0 = none).</summary>
    public int Damage { get; init; }

    /// <summary>Essence runes: damage type.</summary>
    public DamageType DamageType { get; init; } = DamageType.Primal;

    /// <summary>Essence runes: a status effect it puts on what it reaches, if any (an id in <c>effects.json</c>).</summary>
    public string? Effect { get; init; }

    /// <summary>Essence runes: health it heals over the effect's time (wards).</summary>
    public int Heal { get; init; }

    /// <summary>The school it belongs to (resonance), e.g. <c>primal</c>.</summary>
    public string School { get; init; } = "primal";

    /// <summary>Essence runes: poise of the hit (a target with less is staggered; Storm).</summary>
    public int Poise { get; init; }

    /// <summary>Essence runes: share of the damage dealt the caster heals, in percent (Umbral).</summary>
    public int DrainPercent { get; init; }

    /// <summary>Essence runes: damage the shield it puts on takes before health (Ember, with the <c>shielded</c> effect).</summary>
    public int Shield { get; init; }
}

/// <summary>A spell: a form rune and an essence rune with a cost and a cast time (combat spec §8), from <c>spells.json</c>.</summary>
public sealed record SpellDefinition
{
    /// <summary>Stable id, e.g. <c>thornbolt</c>; it is also the id of the move the spell becomes.</summary>
    public required string Id { get; init; }

    /// <summary>The form rune's id and the essence rune's id.</summary>
    public required IReadOnlyList<string> Runes { get; init; }

    /// <summary>Mana cost.</summary>
    public required int Mana { get; init; }

    /// <summary>Ticks of casting before it takes effect (the telegraph).</summary>
    public required int CastTicks { get; init; }

    /// <summary>Ticks after it takes effect before the caster can act again.</summary>
    public int RecoveryTicks { get; init; } = 6;
}

/// <summary>A status effect (combat spec §7), from <c>data/combat/effects.json</c>. Effects refresh, never stack (v1).</summary>
public sealed record EffectDefinition
{
    /// <summary>Stable id, one of <see cref="DataRegistryLoader.KnownEffects"/>.</summary>
    public required string Id { get; init; }

    /// <summary>How long it lasts, in ticks.</summary>
    public required int Ticks { get; init; }

    /// <summary>Movement speed while it lasts, in percent (0 = cannot move, 100 = unchanged, above 100 = faster).</summary>
    public int SpeedPercent { get; init; } = 100;

    /// <summary>Burning: the damage it does over its whole time.</summary>
    public int Damage { get; init; }

    /// <summary>Weakened: damage the bearer deals, in percent; warded: damage the bearer takes, in percent.</summary>
    public int DamagePercent { get; init; } = 100;

    /// <summary>Quickened: stamina comes back at this percent of the normal rate.</summary>
    public int RegenPercent { get; init; } = 100;

    /// <summary>Tags for combos (combat spec §10), e.g. <c>rooted</c>.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];
}
