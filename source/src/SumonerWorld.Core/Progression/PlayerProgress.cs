using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Data.Definitions;

namespace SumonerWorld.Core.Progression;

/// <summary>
/// A player's class (for life), Summoner Rank, level and experience (GDD §5, §11; M2-09). Ints only, so it has no padding (hashed
/// raw). Until the Academy's ranking quest (M2-21) every player is an Elf of rank F.
/// </summary>
/// <param name="Class">A <see cref="SummonerClass"/>.</param>
/// <param name="Rank">A <see cref="Progression.Rank"/>: sets the level cap.</param>
/// <param name="Level">1 … cap.</param>
/// <param name="Xp">Total experience.</param>
public record struct PlayerProgress(int Class, int Rank, int Level, int Xp)
{
    /// <summary>A new player: Elf, rank F, level 1.</summary>
    public static PlayerProgress New => new((int)SummonerClass.Elf, (int)Progression.Rank.F, 1, 0);
}

/// <summary>What a Pact offer knows about the two sides beyond their minds (companions spec §2, M2-09).</summary>
/// <param name="PlayerClass">The summoner's class.</param>
/// <param name="PlayerLevel">The summoner's level.</param>
/// <param name="CreatureClass">The creature's class (its species').</param>
/// <param name="CreatureLevel">The creature's level.</param>
public readonly record struct PactConditions(SummonerClass PlayerClass, int PlayerLevel, SummonerClass CreatureClass, int CreatureLevel)
{
    private const int FarBelowBonus = 600;
    private const int BelowNinetyBonus = 100;
    private const int NearHalfBonus = 400;
    private const int PenaltyPerLevelAbove = 30;
    private const int Percent = 100;
    private const int Half = 50;
    private const int Ninety = 90;

    /// <summary>A creature of another class never bonds (companions spec §2).</summary>
    public bool WrongClass => PlayerClass != CreatureClass;

    /// <summary>
    /// The level difference's part of the Pact score (companions spec §2): +600 at or below half the summoner's level, from +400
    /// near half down to +100 at 90% (straight line), 0 up to the summoner's level, −30 per level above it.
    /// </summary>
    public int LevelBonus
    {
        get
        {
            int player = Math.Max(1, PlayerLevel);
            int creature = Math.Max(1, CreatureLevel);
            if (creature > player)
            {
                return -PenaltyPerLevelAbove * (creature - player);
            }

            int share = creature * Percent / player;
            if (creature * Percent <= player * Half)
            {
                return FarBelowBonus;
            }

            return share > Ninety ? 0 : BelowNinetyBonus + ((NearHalfBonus - BelowNinetyBonus) * (Ninety - share) / (Ninety - Half));
        }
    }
}

/// <summary>Player growth (GDD §5, M2-09): experience to levels capped by rank, and the stats each level gives.</summary>
public static class PlayerGrowth
{
    /// <summary>Adds experience; returns true when the level went up (the player's stats are then raised).</summary>
    public static bool Earn(GrowthRules rules, ref PlayerProgress progress, ref Combatant combatant, CombatStatsDefinition baseStats, int xp)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (xp <= 0)
        {
            return false;
        }

        progress.Xp += xp;
        int level = rules.LevelFor(progress.Xp, (Rank)progress.Rank);
        if (level <= progress.Level)
        {
            return false;
        }

        progress.Level = level;
        Restat(rules, progress, ref combatant, baseStats);
        return true;
    }

    /// <summary>Sets a player's health and power from the base stats and their level (as a grown creature's).</summary>
    public static void Restat(GrowthRules rules, in PlayerProgress progress, ref Combatant combatant, CombatStatsDefinition baseStats)
    {
        ArgumentNullException.ThrowIfNull(rules);
        rules.Apply(ref combatant, baseStats, new CreatureGrowth(progress.Rank, progress.Level, (int)LifeStage.Grown, 0, 0, 0, 0));
    }
}
