using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Data.Definitions;

namespace SumonerWorld.Core.Progression;

/// <summary>
/// Summoner ranks and creature tiers, one scale (GDD §5, §11; companions spec §3). Each step raises the level cap by 10. Values are
/// saved: never renumber.
/// </summary>
public enum Rank : byte
{
    /// <summary>F: level cap 10.</summary>
    F = 0,

    /// <summary>E: 20.</summary>
    E = 1,

    /// <summary>D: 30.</summary>
    D = 2,

    /// <summary>C: 40.</summary>
    C = 3,

    /// <summary>B: 50.</summary>
    B = 4,

    /// <summary>A: 60.</summary>
    A = 5,

    /// <summary>S: 70.</summary>
    S = 6,

    /// <summary>SS: 80.</summary>
    SS = 7,

    /// <summary>SSS: 90.</summary>
    SSS = 8,

    /// <summary>God tier: 100.</summary>
    God = 9,
}

/// <summary>The four summoner classes (GDD §11); every species belongs to one. Values are saved: never renumber.</summary>
public enum SummonerClass : byte
{
    /// <summary>Elves (the slice's class).</summary>
    Elf = 0,

    /// <summary>Humans.</summary>
    Human = 1,

    /// <summary>Necromancers.</summary>
    Necromancer = 2,

    /// <summary>Dwarves.</summary>
    Dwarf = 3,
}

/// <summary>A creature's life stage (companions spec §4). Creatures never die of old age. Values are sent and saved.</summary>
public enum LifeStage : byte
{
    /// <summary>Young: the base look.</summary>
    Young = 0,

    /// <summary>Grown: bigger, more health, one more skill slot.</summary>
    Grown = 1,

    /// <summary>Elder: the peak of its life; more power, the signature skill.</summary>
    Elder = 2,
}

/// <summary>Experience roles (companions spec §3).</summary>
public enum XpRole
{
    /// <summary>Fighting at the owner's side.</summary>
    Combat,

    /// <summary>Sanctum tasks and gathering.</summary>
    Work,

    /// <summary>Walking with the owner and scouting.</summary>
    Travel,
}

/// <summary>
/// A creature's growth (companions spec §3–4, M2-09): tier, level, stage and experience per role. <c>TravelCm</c> collects the
/// distance walked while summoned until it makes a point of travel experience. Ints only, so it has no padding (hashed raw).
/// </summary>
/// <param name="Tier">A <see cref="Rank"/>: sets the level cap.</param>
/// <param name="Level">1 … cap.</param>
/// <param name="Stage">A <see cref="LifeStage"/>.</param>
/// <param name="CombatXp">Combat experience.</param>
/// <param name="WorkXp">Work experience.</param>
/// <param name="TravelXp">Travel experience.</param>
/// <param name="TravelCm">Distance walked toward the next travel point.</param>
public record struct CreatureGrowth(int Tier, int Level, int Stage, int CombatXp, int WorkXp, int TravelXp, int TravelCm)
{
    /// <summary>All experience.</summary>
    public readonly int TotalXp => CombatXp + WorkXp + TravelXp;
}

/// <summary>Growth arithmetic from the data (companions spec §3–4): level curve, caps, stage conditions and stats.</summary>
public sealed class GrowthRules
{
    /// <summary>Highest level of any tier.</summary>
    public const int MaxLevel = 100;

    private const int Percent = 100;
    private const int LevelsPerRank = 10;

    private readonly GrowthDefinition _definition;
    private readonly int[] _xpForLevel = new int[MaxLevel + 2];

    /// <summary>Builds the level table from the data: level <c>n</c> needs <c>factor × (n − 1)^exponent</c> experience (rounded).</summary>
    public GrowthRules(GrowthDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _definition = definition;
        double exponent = definition.XpExponentPercent / (double)Percent;
        for (int level = 1; level <= MaxLevel + 1; level++)
        {
            _xpForLevel[level] = (int)Math.Round(definition.XpFactor * Math.Pow(level - 1, exponent), MidpointRounding.AwayFromZero);
        }
    }

    /// <summary>The data.</summary>
    public GrowthDefinition Definition => _definition;

    /// <summary>
    /// A wild creature's growth: its species' tier, a level in the species' range picked from its stable id (no random draw, so
    /// spawning does not shift the world's random stream), grown.
    /// </summary>
    public static CreatureGrowth Wild(SpeciesDefinition species, ulong stableId)
    {
        ArgumentNullException.ThrowIfNull(species);
        int span = species.Levels.Max - species.Levels.Min + 1;
        int level = species.Levels.Min + (int)((stableId * 0x9E3779B97F4A7C15UL >> 40) % (ulong)span);
        return new CreatureGrowth((int)species.Tier, level, (int)LifeStage.Grown, 0, 0, 0, 0);
    }

    /// <summary>Growth for a creature saved before growth existed: pacted ones start at level 1 and young (companions spec §9).</summary>
    public static CreatureGrowth Migrated(SpeciesDefinition species, ulong stableId, bool pacted) =>
        pacted ? new CreatureGrowth((int)species.Tier, 1, (int)LifeStage.Young, 0, 0, 0, 0) : Wild(species, stableId);

    /// <summary>The highest level of a rank or tier (10 … 100).</summary>
    public static int Cap(Rank rank) => ((int)rank + 1) * LevelsPerRank;

    /// <summary>Total experience a level needs.</summary>
    public int XpFor(int level) => _xpForLevel[Math.Clamp(level, 1, MaxLevel + 1)];

    /// <summary>The level <paramref name="totalXp"/> reaches, at most the tier's cap.</summary>
    public int LevelFor(int totalXp, Rank tier)
    {
        int cap = Cap(tier);
        int level = 1;
        while (level < cap && totalXp >= _xpForLevel[level + 1])
        {
            level++;
        }

        return level;
    }

    /// <summary>The stage a companion may grow into now (it only ever grows; bond level as in AI Bible §10.3).</summary>
    public LifeStage StageAllowed(in CreatureGrowth growth, int bondLevel)
    {
        int cap = Cap((Rank)growth.Tier);
        var stage = (LifeStage)growth.Stage;
        if (stage == LifeStage.Young && growth.Level * Percent >= cap * _definition.GrownAtCapPercent && bondLevel >= _definition.GrownBondLevel)
        {
            return LifeStage.Grown;
        }

        if (stage == LifeStage.Grown && growth.Level * Percent >= cap * _definition.ElderAtCapPercent && bondLevel >= _definition.ElderBondLevel)
        {
            return LifeStage.Elder;
        }

        return stage;
    }

    /// <summary>
    /// Sets a creature's combat stats from its species' base, level and stage, keeping its health share (species stats are a grown
    /// creature's at level 1: a young one has <see cref="GrowthDefinition.GrownHealthPercent"/> less health, an elder
    /// <see cref="GrowthDefinition.ElderPowerPercent"/> more power).
    /// </summary>
    public void Apply(ref Combatant combatant, CombatStatsDefinition species, in CreatureGrowth growth)
    {
        ArgumentNullException.ThrowIfNull(species);
        int levels = growth.Level - 1;
        var stage = (LifeStage)growth.Stage;
        long health = (long)species.Health * (Percent + (_definition.HealthPercentPerLevel * levels)) / Percent;
        health = stage == LifeStage.Young ? health * Percent / (Percent + _definition.GrownHealthPercent) : health;
        long power = (long)species.Power * (Percent + (_definition.PowerPercentPerLevel * levels)) / Percent;
        power = stage == LifeStage.Elder ? power * (Percent + _definition.ElderPowerPercent) / Percent : power;

        int maxHealth = (int)Math.Max(1, health);
        long share = combatant.MaxHealth > 0 ? (long)combatant.Health * maxHealth / combatant.MaxHealth : maxHealth;
        combatant.Health = combatant.Health == 0 ? 0 : (int)Math.Max(1, share);
        combatant.MaxHealth = maxHealth;
        combatant.Power = (int)power;
    }
}
