using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Data;
using SumonerWorld.Core.Data.Definitions;

namespace SumonerWorld.Core.Progression;

/// <summary>
/// A creature's skills (companions spec §5, M2-10): the learned nodes of its species' tree (bit = node index), the points spent,
/// since when it has had points it did not spend (it then picks for itself), and until when it shows reluctance after learning
/// a skill it did not like. No padding (hashed raw).
/// </summary>
/// <param name="Learned">Learned node bits.</param>
/// <param name="UnspentSince">Tick it got points it has not spent yet (0 = none).</param>
/// <param name="ReluctantUntil">Tick its reluctance emote ends.</param>
/// <param name="Spent">Skill points spent.</param>
/// <param name="PickedItself">Skills it picked for itself.</param>
public record struct CreatureSkills(ulong Learned, ulong UnspentSince, ulong ReluctantUntil, int Spent, int PickedItself)
{
    /// <summary>True when node <paramref name="index"/> is learned.</summary>
    public readonly bool Knows(int index) => (uint)index < SkillTreeDefinition.MaxNodes && (Learned & (1UL << index)) != 0;
}

/// <summary>Where a node stands for one creature.</summary>
public enum SkillState : byte
{
    /// <summary>Learned.</summary>
    Learned = 0,

    /// <summary>Can be learned now.</summary>
    Available = 1,

    /// <summary>Its level is too low.</summary>
    NeedsLevel = 2,

    /// <summary>Its bond level is too low.</summary>
    NeedsBond = 3,

    /// <summary>It has not grown enough.</summary>
    NeedsStage = 4,

    /// <summary>An earlier skill of the branch is missing.</summary>
    NeedsSkill = 5,

    /// <summary>Not enough skill points.</summary>
    NoPoints = 6,

    /// <summary>No such node.</summary>
    Unknown = 7,
}

/// <summary>Skill tree arithmetic (companions spec §5): points, slots, branch affinity, cost, passives and active moves.</summary>
public static class SkillRules
{
    /// <summary>Affinity at or above this makes a branch warm (it likes it).</summary>
    public const int WarmAt = 20;

    /// <summary>Affinity at or below this makes a branch cold: its skills cost 2 points and it shows reluctance.</summary>
    public const int ColdAt = -20;

    /// <summary>Levels per skill point.</summary>
    public const int LevelsPerPoint = 2;

    private const int Percent = 100;
    private const int Divisor = 10;
    private const int BaseSlots = 2;

    /// <summary>Points it can spend now: one per two levels, minus what it spent.</summary>
    public static int Points(in CreatureGrowth growth, in CreatureSkills skills) => Math.Max(0, (growth.Level / LevelsPerPoint) - skills.Spent);

    /// <summary>Active skills it can use at once: 2, one more when grown, one more as an elder.</summary>
    public static int Slots(LifeStage stage) => BaseSlots + (int)stage;

    /// <summary>How much it likes a branch: its traits and the share of each role in its experience.</summary>
    public static int Affinity(SkillBranchDefinition branch, Personality personality, in CreatureGrowth growth)
    {
        ArgumentNullException.ThrowIfNull(branch);
        int affinity = 0;
        foreach (PersonalityFactor factor in branch.Traits)
        {
            affinity += factor.Factor * personality.Get(factor.Trait) / Divisor;
        }

        int total = growth.TotalXp;
        foreach (RoleFactor factor in branch.Roles)
        {
            int xp = factor.Role switch
            {
                XpRole.Combat => growth.CombatXp,
                XpRole.Work => growth.WorkXp,
                _ => growth.TravelXp,
            };
            affinity += total == 0 ? 0 : factor.Factor * (xp * Percent / total) / Divisor;
        }

        return affinity;
    }

    /// <summary>Points a node of a branch with this affinity costs.</summary>
    public static int Cost(int affinity) => affinity <= ColdAt ? 2 : 1;

    /// <summary>The branch index of a node.</summary>
    public static int BranchOf(SkillTreeDefinition tree, int node)
    {
        ArgumentNullException.ThrowIfNull(tree);
        string branch = tree.Nodes[node].Branch;
        for (int i = 0; i < tree.Branches.Count; i++)
        {
            if (tree.Branches[i].Id == branch)
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>Where node <paramref name="node"/> stands for a creature.</summary>
    public static SkillState StateOf(
        SkillTreeDefinition? tree, int node, in CreatureGrowth growth, in CreatureSkills skills, int bondLevel, Personality personality)
    {
        if (tree is null || (uint)node >= (uint)tree.Nodes.Count)
        {
            return SkillState.Unknown;
        }

        int affinity = Affinity(tree.Branches[BranchOf(tree, node)], personality, growth);
        return StateOf(tree, node, growth.Level, (LifeStage)growth.Stage, bondLevel, skills.Learned, Points(growth, skills), affinity);
    }

    /// <summary>
    /// Where a node stands, from what the companion list carries (the client uses this): level, stage, bond level, learned bits,
    /// points and the affinity of the node's branch.
    /// </summary>
    public static SkillState StateOf(
        SkillTreeDefinition? tree, int node, int level, LifeStage stage, int bondLevel, ulong learned, int points, int branchAffinity)
    {
        if (tree is null || (uint)node >= (uint)tree.Nodes.Count)
        {
            return SkillState.Unknown;
        }

        if ((learned & (1UL << node)) != 0)
        {
            return SkillState.Learned;
        }

        SkillNodeDefinition definition = tree.Nodes[node];
        if (level < definition.Level)
        {
            return SkillState.NeedsLevel;
        }

        if (bondLevel < definition.BondLevel)
        {
            return SkillState.NeedsBond;
        }

        if (stage < definition.Stage)
        {
            return SkillState.NeedsStage;
        }

        foreach (string required in definition.Requires)
        {
            int index = IndexOf(tree, required);
            if (index < 0 || (learned & (1UL << index)) == 0)
            {
                return SkillState.NeedsSkill;
            }
        }

        return points < Cost(branchAffinity) ? SkillState.NoPoints : SkillState.Available;
    }

    /// <summary>The index of a node id, −1 when the tree has none.</summary>
    public static int IndexOf(SkillTreeDefinition tree, string id)
    {
        ArgumentNullException.ThrowIfNull(tree);
        for (int i = 0; i < tree.Nodes.Count; i++)
        {
            if (tree.Nodes[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Adds the learned passives to stats already set from level and stage: health and power in percent (keeping the health
    /// share), guard, poise and stamina on top of the base.
    /// </summary>
    public static void ApplyPassives(SkillTreeDefinition? tree, ulong learned, CombatStatsDefinition baseStats, ref Combatant combatant)
    {
        ArgumentNullException.ThrowIfNull(baseStats);
        (int health, int power, int guard, int poise, int stamina) = (0, 0, 0, 0, 0);
        for (int i = 0; tree is not null && i < tree.Nodes.Count; i++)
        {
            if ((learned & (1UL << i)) == 0 || tree.Nodes[i].Passive is not SkillPassiveDefinition passive)
            {
                continue;
            }

            switch (passive.Stat)
            {
                case SkillStat.Health:
                    health += passive.Amount;
                    break;
                case SkillStat.Power:
                    power += passive.Amount;
                    break;
                case SkillStat.Guard:
                    guard += passive.Amount;
                    break;
                case SkillStat.Poise:
                    poise += passive.Amount;
                    break;
                default:
                    stamina += passive.Amount;
                    break;
            }
        }

        int maxHealth = (int)((long)combatant.MaxHealth * (Percent + health) / Percent);
        combatant.Health = combatant.Health == 0 || combatant.MaxHealth == 0 ? combatant.Health : Math.Max(1, (int)((long)combatant.Health * maxHealth / combatant.MaxHealth));
        combatant.MaxHealth = maxHealth;
        combatant.Power = combatant.Power * (Percent + power) / Percent;
        combatant.Guard = baseStats.Guard + guard;
        combatant.Poise = baseStats.Poise + poise;
        combatant.MaxStamina = baseStats.Stamina + stamina;
        combatant.Stamina = Math.Min(combatant.Stamina, combatant.MaxStamina);
    }

    /// <summary>
    /// The moves of its active skills, in tree order, at most as many as its slots; returns how many were written to
    /// <paramref name="moves"/> (move indices).
    /// </summary>
    public static int ActiveMoves(IDataRegistry data, SkillTreeDefinition? tree, ulong learned, LifeStage stage, Span<int> moves)
    {
        ArgumentNullException.ThrowIfNull(data);
        int count = 0;
        int slots = Math.Min(Slots(stage), moves.Length);
        for (int i = 0; tree is not null && i < tree.Nodes.Count && count < slots; i++)
        {
            if ((learned & (1UL << i)) != 0 && tree.Nodes[i].Move is string move)
            {
                moves[count++] = data.MoveIndex(move);
            }
        }

        return count;
    }
}
