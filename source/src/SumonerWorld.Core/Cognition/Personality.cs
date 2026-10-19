using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Randomness;

namespace SumonerWorld.Core.Cognition;

/// <summary>A creature's four trait values (AI Bible §3), each in [−100, 100].</summary>
/// <param name="Boldness">Timid (−100) to bold (+100).</param>
/// <param name="Loyalty">Independent (−100) to loyal (+100).</param>
/// <param name="Curiosity">Wary (−100) to curious (+100).</param>
/// <param name="Temper">Gentle (−100) to aggressive (+100).</param>
public readonly record struct Personality(sbyte Boldness, sbyte Loyalty, sbyte Curiosity, sbyte Temper)
{
    /// <summary>Lowest trait value.</summary>
    public const int MinTrait = -100;

    /// <summary>Highest trait value.</summary>
    public const int MaxTrait = 100;

    /// <summary>Largest total drift from the birth value (AI Bible §3).</summary>
    public const int MaxDrift = 25;

    /// <summary>Rolls birth traits uniformly in mean ± spread for each axis, from the creature's own stream (AI Bible §3).</summary>
    public static Personality Roll(SpeciesDefinition species, IRandom random)
    {
        ArgumentNullException.ThrowIfNull(species);
        ArgumentNullException.ThrowIfNull(random);

        return new Personality(
            RollTrait(species.Boldness, random),
            RollTrait(species.Loyalty, random),
            RollTrait(species.Curiosity, random),
            RollTrait(species.Temper, random));
    }

    /// <summary>The value of one trait.</summary>
    public int Get(PersonalityTrait trait) => trait switch
    {
        PersonalityTrait.Boldness => Boldness,
        PersonalityTrait.Loyalty => Loyalty,
        PersonalityTrait.Curiosity => Curiosity,
        PersonalityTrait.Temper => Temper,
        _ => throw new ArgumentOutOfRangeException(nameof(trait), trait, "Unknown trait."),
    };

    /// <summary>
    /// This personality shifted by <paramref name="amount"/> on one trait, kept within ±<see cref="MaxDrift"/> of
    /// <paramref name="birth"/> and within [−100, 100].
    /// </summary>
    public Personality Drift(PersonalityTrait trait, int amount, Personality birth)
    {
        int birthValue = birth.Get(trait);
        int min = Math.Max(MinTrait, birthValue - MaxDrift);
        int max = Math.Min(MaxTrait, birthValue + MaxDrift);
        sbyte value = (sbyte)Math.Clamp(Get(trait) + amount, min, max);

        return trait switch
        {
            PersonalityTrait.Boldness => this with { Boldness = value },
            PersonalityTrait.Loyalty => this with { Loyalty = value },
            PersonalityTrait.Curiosity => this with { Curiosity = value },
            PersonalityTrait.Temper => this with { Temper = value },
            _ => throw new ArgumentOutOfRangeException(nameof(trait), trait, "Unknown trait."),
        };
    }

    private static sbyte RollTrait(TraitRange range, IRandom random)
    {
        int value = random.NextInt(range.Mean - range.Spread, range.Mean + range.Spread + 1);
        return (sbyte)Math.Clamp(value, MinTrait, MaxTrait);
    }
}
