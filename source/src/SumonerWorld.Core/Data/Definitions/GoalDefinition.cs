namespace SumonerWorld.Core.Data.Definitions;

/// <summary>What a consideration reads (AI Bible §7.2). Every input is normalised to [0, 1000].</summary>
public enum ConsiderationInput
{
    /// <summary>Always 1000 (use with a curve for a fixed factor).</summary>
    Constant,

    /// <summary>Hunger need, 0–100 × 10.</summary>
    NeedHunger,

    /// <summary>Rest need, 0–100 × 10.</summary>
    NeedRest,

    /// <summary>Safety need, 0–100 × 10.</summary>
    NeedSafety,

    /// <summary>Social need, 0–100 × 10.</summary>
    NeedSocial,

    /// <summary>Territory need, 0–100 × 10.</summary>
    NeedTerritory,

    /// <summary>Fear emotion, 0–100 × 10.</summary>
    EmotionFear,

    /// <summary>Anger emotion, 0–100 × 10.</summary>
    EmotionAnger,

    /// <summary>Joy emotion, 0–100 × 10.</summary>
    EmotionJoy,

    /// <summary>Curiosity emotion, 0–100 × 10.</summary>
    EmotionCuriosity,

    /// <summary>Stress emotion, 0–100 × 10.</summary>
    EmotionStress,

    /// <summary>1000 if the creature is focused on a subject, else 0.</summary>
    HasTarget,

    /// <summary>Trust in the focused subject, −100…100 mapped to 0…1000 (500 = neutral).</summary>
    TargetTrust,

    /// <summary>Fear of the focused subject, −100…100 mapped to 0…1000.</summary>
    TargetFear,

    /// <summary>Affection for the focused subject, −100…100 mapped to 0…1000.</summary>
    TargetAffection,

    /// <summary>Respect for the focused subject, −100…100 mapped to 0…1000.</summary>
    TargetRespect,

    /// <summary>Distance to the focused subject, 0 (touching) to 1000 (edge of perception).</summary>
    TargetDistance,

    /// <summary>Own health, 0 (dying) to 1000 (full).</summary>
    Health,

    /// <summary>Time of day, 0 (start of day) to 1000 (end of night).</summary>
    TimeOfDay,
}

/// <summary>Shape of a response curve (AI Bible §7.2).</summary>
public enum CurveType
{
    /// <summary><c>offset + slope × x / 1000</c>.</summary>
    Linear,

    /// <summary><c>offset + slope × x² / 1,000,000</c>.</summary>
    Quadratic,

    /// <summary>S-curve from 0 to 1000 across <c>centre ± width</c> (integer smoothstep; stands in for logistic).</summary>
    Logistic,

    /// <summary><c>high</c> when <c>x ≥ threshold</c>, else <c>low</c>.</summary>
    Step,
}

/// <summary>A response curve: maps an input in [0, 1000] to a score factor in [0, 1000]. Unused fields stay 0.</summary>
public sealed record CurveDefinition
{
    /// <summary>Curve shape.</summary>
    public required CurveType Type { get; init; }

    /// <summary>Linear/Quadratic: slope (output points per 1000 input).</summary>
    public int Slope { get; init; }

    /// <summary>Linear/Quadratic: output at input 0.</summary>
    public int Offset { get; init; }

    /// <summary>Logistic: input at the middle of the S.</summary>
    public int Centre { get; init; }

    /// <summary>Logistic: half the width of the S (output is 0 below centre − width, 1000 above centre + width).</summary>
    public int Width { get; init; }

    /// <summary>Step: input at which the output switches to <see cref="High"/>.</summary>
    public int Threshold { get; init; }

    /// <summary>Step: output below the threshold.</summary>
    public int Low { get; init; }

    /// <summary>Step: output at or above the threshold.</summary>
    public int High { get; init; }

    /// <summary>Mirror the output (1000 − value), e.g. "the less trust, the more…".</summary>
    public bool Invert { get; init; }
}

/// <summary>One factor of a goal's score.</summary>
public sealed record ConsiderationDefinition
{
    /// <summary>What is read.</summary>
    public required ConsiderationInput Input { get; init; }

    /// <summary>How the input becomes a factor.</summary>
    public required CurveDefinition Curve { get; init; }
}

/// <summary>How one personality trait scales a goal's score (AI Bible §7.2 personality modifier).</summary>
public sealed record PersonalityFactor
{
    /// <summary>Largest allowed factor per trait.</summary>
    public const int MaxFactor = 10;

    /// <summary>The trait.</summary>
    public required PersonalityTrait Trait { get; init; }

    /// <summary>Per-mille change per trait point: modifier = 1000 + Σ factor × trait. E.g. −5 means a fully bold creature (+100) scores this goal at 50%.</summary>
    public required int Factor { get; init; }
}

/// <summary>A goal the utility AI can choose (AI Bible §7.1), loaded from <c>data/ai/goals.json</c>.</summary>
public sealed record GoalDefinition
{
    /// <summary>Highest allowed base weight.</summary>
    public const int MaxWeight = 2000;

    /// <summary>Stable id (never reused); 0 means idle.</summary>
    public required ushort Id { get; init; }

    /// <summary>Stable name, e.g. <c>Flee</c>; species refer to goals by key.</summary>
    public required string Key { get; init; }

    /// <summary>Base importance, in [1, 2000] (1000 = normal).</summary>
    public required int Weight { get; init; }

    /// <summary>True if this goal may pre-empt the current goal between decision ticks (AI Bible §7.2 interrupts).</summary>
    public required bool Interrupt { get; init; }

    /// <summary>Personality scaling of the score.</summary>
    public required IReadOnlyList<PersonalityFactor> Personality { get; init; }

    /// <summary>Factors multiplied together; at least one.</summary>
    public required IReadOnlyList<ConsiderationDefinition> Considerations { get; init; }
}
