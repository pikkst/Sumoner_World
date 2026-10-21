using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Progression;

namespace SumonerWorld.Core.Data;

/// <summary>Validation of the skill trees (companions spec §5, M2-10).</summary>
public static partial class DataRegistryLoader
{
    private const int MaxSkillAmount = 500;

    private static void ValidateSkillTree(SkillTreeDefinition tree, string where, List<string> errors)
    {
        if (tree.Branches is null || tree.Branches.Count is 0 or > SkillTreeDefinition.MaxBranches)
        {
            errors.Add($"{where}: a tree has 1–{SkillTreeDefinition.MaxBranches} branches.");
            return;
        }

        if (tree.Nodes is null || tree.Nodes.Count is 0 or > SkillTreeDefinition.MaxNodes)
        {
            errors.Add($"{where}: a tree has 1–{SkillTreeDefinition.MaxNodes} nodes.");
            return;
        }

        var branches = new HashSet<string>(StringComparer.Ordinal);
        foreach (SkillBranchDefinition branch in tree.Branches)
        {
            if (!IsSnakeCaseId(branch.Id) || !branches.Add(branch.Id))
            {
                errors.Add($"{where}: branch id '{branch.Id}' must be snake_case and unique.");
            }

            foreach (PersonalityFactor factor in branch.Traits)
            {
                CheckRange(factor.Factor, -PersonalityFactor.MaxFactor, PersonalityFactor.MaxFactor, $"branches.{branch.Id}.traits", where, errors);
            }

            foreach (RoleFactor factor in branch.Roles)
            {
                CheckRange(factor.Factor, -PersonalityFactor.MaxFactor, PersonalityFactor.MaxFactor, $"branches.{branch.Id}.roles", where, errors);
            }
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (SkillNodeDefinition node in tree.Nodes)
        {
            string at = $"{where}: node '{node.Id}'";
            if (!IsSnakeCaseId(node.Id) || !seen.Add(node.Id))
            {
                errors.Add($"{at}: id must be snake_case and unique in the tree.");
            }

            if (!branches.Contains(node.Branch))
            {
                errors.Add($"{at}: unknown branch '{node.Branch}'.");
            }

            CheckRange(node.Level, 1, GrowthRules.MaxLevel, "level", at, errors);
            CheckRange(node.BondLevel, 0, MaxMoveTicks, "bondLevel", at, errors);
            if ((node.Move is null) == (node.Passive is null))
            {
                errors.Add($"{at}: a node gives exactly one of 'move' or 'passive'.");
            }

            if (node.Passive is not null)
            {
                CheckRange(node.Passive.Amount, 1, MaxSkillAmount, "passive.amount", at, errors);
            }

            foreach (string required in node.Requires)
            {
                if (!seen.Contains(required))
                {
                    errors.Add($"{at}: requires '{required}', which must be an earlier node of the tree.");
                }
            }
        }
    }

    // Every tree names a known species (at most one tree each) and teaches known moves.
    private static void ValidateSkillTreeReferences(
        List<(SkillTreeDefinition Entry, string Where)> trees, List<SpeciesDefinition> species, List<(MoveDefinition Entry, string Where)> moves, List<string> errors)
    {
        var known = new HashSet<string>(species.Select(definition => definition.Id), StringComparer.Ordinal);
        var moveIds = new HashSet<string>(moves.Select(move => move.Entry.Id), StringComparer.Ordinal);
        var owners = new HashSet<string>(StringComparer.Ordinal);
        foreach ((SkillTreeDefinition tree, string where) in trees)
        {
            if (!known.Contains(tree.Species) || !owners.Add(tree.Species))
            {
                errors.Add($"{where}: species '{tree.Species}' is unknown or already has a tree.");
            }

            foreach (SkillNodeDefinition node in tree.Nodes ?? [])
            {
                if (node.Move is string move && !moveIds.Contains(move))
                {
                    errors.Add($"{where}: node '{node.Id}' teaches unknown move '{move}'.");
                }
            }
        }
    }
}
