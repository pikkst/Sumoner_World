using SumonerWorld.Core.Progression;

namespace SumonerWorld.Core.Data.Definitions;

/// <summary>
/// One species' skill tree (companions spec §5, M2-10), loaded from <c>data/companions/skills/&lt;species&gt;.json</c>: about nine
/// nodes in three branches. A node's position in the file is its saved bit and must never change.
/// </summary>
public sealed record SkillTreeDefinition
{
    /// <summary>Most nodes in a tree (they are saved as a bit mask).</summary>
    public const int MaxNodes = 32;

    /// <summary>Most branches in a tree.</summary>
    public const int MaxBranches = 4;

    /// <summary>The species id it belongs to.</summary>
    public required string Species { get; init; }

    /// <summary>The branches, in display order.</summary>
    public required IReadOnlyList<SkillBranchDefinition> Branches { get; init; }

    /// <summary>The nodes; index = saved bit.</summary>
    public required IReadOnlyList<SkillNodeDefinition> Nodes { get; init; }
}

/// <summary>
/// A branch and what makes a creature like it (companions spec §5): its traits and the roles it has done most. Affinity =
/// Σ factor × trait / 10 + Σ factor × share of that role's experience (0–100) / 10.
/// </summary>
public sealed record SkillBranchDefinition
{
    /// <summary>Stable id, e.g. <c>vanguard</c>.</summary>
    public required string Id { get; init; }

    /// <summary>How traits move the affinity (factors −10…10).</summary>
    public IReadOnlyList<PersonalityFactor> Traits { get; init; } = [];

    /// <summary>How the roles it did most move the affinity.</summary>
    public IReadOnlyList<RoleFactor> Roles { get; init; } = [];
}

/// <summary>How much a role's share of experience counts toward a branch.</summary>
public sealed record RoleFactor
{
    /// <summary>The role.</summary>
    public required XpRole Role { get; init; }

    /// <summary>Factor −10…10.</summary>
    public required int Factor { get; init; }
}

/// <summary>A skill: what unlocks it and what it gives (a move from the combat data or a passive).</summary>
public sealed record SkillNodeDefinition
{
    /// <summary>Stable id in snake_case, e.g. <c>horn_toss</c>.</summary>
    public required string Id { get; init; }

    /// <summary>Its branch's id.</summary>
    public required string Branch { get; init; }

    /// <summary>Lowest creature level.</summary>
    public int Level { get; init; } = 1;

    /// <summary>Lowest bond level (AI Bible §10.3).</summary>
    public int BondLevel { get; init; }

    /// <summary>Lowest life stage.</summary>
    public LifeStage Stage { get; init; } = LifeStage.Young;

    /// <summary>Node ids that must be learned first.</summary>
    public IReadOnlyList<string> Requires { get; init; } = [];

    /// <summary>A move it teaches (an active skill), if any.</summary>
    public string? Move { get; init; }

    /// <summary>A passive bonus it gives, if any.</summary>
    public SkillPassiveDefinition? Passive { get; init; }
}

/// <summary>A stat a passive skill raises.</summary>
public enum SkillStat
{
    /// <summary>Health, in percent.</summary>
    Health,

    /// <summary>Power, in percent.</summary>
    Power,

    /// <summary>Guard, flat.</summary>
    Guard,

    /// <summary>Poise, flat.</summary>
    Poise,

    /// <summary>Stamina, flat.</summary>
    Stamina,
}

/// <summary>A passive bonus.</summary>
public sealed record SkillPassiveDefinition
{
    /// <summary>The stat.</summary>
    public required SkillStat Stat { get; init; }

    /// <summary>How much (percent for health and power, flat otherwise).</summary>
    public required int Amount { get; init; }
}
