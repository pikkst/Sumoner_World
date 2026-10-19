namespace SumonerWorld.Core.Cognition.Opinions;

/// <summary>A creature's four attitudes toward one subject (AI Bible §5), each in [−100, 100].</summary>
/// <param name="Trust">Drives obedience, Pact acceptance and re-pact consent.</param>
/// <param name="Fear">Drives fleeing, submission and hesitation.</param>
/// <param name="Affection">Drives following distance, autonomous protection and grief.</param>
/// <param name="Respect">Drives accepting hard commands and apex Pacts.</param>
public readonly record struct Opinion(int Trust, int Fear, int Affection, int Respect)
{
    /// <summary>Lowest attitude value.</summary>
    public const int Min = -100;

    /// <summary>Highest attitude value.</summary>
    public const int Max = 100;

    /// <summary>No opinion at all.</summary>
    public static Opinion Neutral => default;

    /// <summary>How strongly the creature feels about the subject (used to decide which opinions to keep, AI Bible §4.4).</summary>
    public int Intensity => Math.Abs(Trust) + Math.Abs(Fear) + Math.Abs(Affection);

    /// <summary>Each attitude clamped to [−100, 100].</summary>
    public Opinion Clamped() => new(
        Math.Clamp(Trust, Min, Max), Math.Clamp(Fear, Min, Max), Math.Clamp(Affection, Min, Max), Math.Clamp(Respect, Min, Max));
}
