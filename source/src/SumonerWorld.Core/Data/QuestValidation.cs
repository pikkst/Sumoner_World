using SumonerWorld.Core.Data.Definitions;

namespace SumonerWorld.Core.Data;

/// <summary>Quests (quests spec, M2-19).</summary>
internal static partial class QuestValidation
{
    private const int MaxSteps = 8;
    private const int MaxQuests = 250;
    private const float MaxRadius = 200;
    private const int MaxStack = 999;
    private const int MaxGold = 1_000_000;

    // Ids, steps, objectives, rewards and the quests required before each (known, and earlier in file order so there are no loops).
    internal static void ValidateQuests(
        List<(QuestDefinition Entry, string Where)> quests,
        List<SpeciesDefinition> species,
        List<ItemDefinition> items,
        AcademyDefinition? academy,
        RegionDefinition? region,
        List<string> errors)
    {
        if (quests.Count > MaxQuests)
        {
            errors.Add($"{quests.Count} quests; at most {MaxQuests}.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var itemIds = items.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var speciesIds = species.Select(definition => definition.Id).ToHashSet(StringComparer.Ordinal);
        var npcIds = (academy?.Npcs ?? []).Concat(region?.People ?? []).Select(npc => npc.Id).ToHashSet(StringComparer.Ordinal);
        foreach (string shared in (academy?.Npcs ?? []).Select(npc => npc.Id).Intersect((region?.People ?? []).Select(npc => npc.Id), StringComparer.Ordinal))
        {
            errors.Add($"Person '{shared}' is both one of the Academy's people and one of the region's.");
        }

        foreach ((QuestDefinition quest, string where) in quests)
        {
            foreach (string required in quest.Requires.Where(required => !ids.Contains(required)))
            {
                errors.Add($"{where}: requires '{required}', which is not a quest earlier in the files.");
            }

            if (quest.Class.Length > 0 && !AcademyDefinition.ClassIds.Contains(quest.Class))
            {
                errors.Add($"{where}: quest '{quest.Id}' names unknown class '{quest.Class}'.");
            }

            if (!DataRegistryLoader.IsSnakeCaseId(quest.Id) || !ids.Add(quest.Id))
            {
                errors.Add($"{where}: id '{quest.Id}' must be snake_case and unique.");
            }

            DataRegistryLoader.CheckRange(quest.MinLevel, 1, Progression.GrowthRules.MaxLevel, "minLevel", where, errors);
            DataRegistryLoader.CheckRange(quest.Steps.Count, 1, MaxSteps, "steps", where, errors);
            for (int s = 0; s < quest.Steps.Count; s++)
            {
                ValidateStep(quest.Steps[s], $"{where}.steps[{s}]", new QuestTargets(speciesIds, itemIds, npcIds), errors);
            }

            DataRegistryLoader.CheckRange(quest.Rewards.Xp, 0, MaxGold, "rewards.xp", where, errors);
            DataRegistryLoader.CheckRange(quest.Rewards.Gold, 0, MaxGold, "rewards.gold", where, errors);
            foreach (RecipePart part in quest.Rewards.Items)
            {
                DataRegistryLoader.CheckItem(part.Item, itemIds, where, errors);
                DataRegistryLoader.CheckRange(part.Count, 1, MaxStack, "rewards.items.count", where, errors);
            }
        }
    }

    private static void ValidateStep(QuestStepDefinition step, string where, QuestTargets known, List<string> errors)
    {
        DataRegistryLoader.CheckRange(step.Objectives.Count, 1, QuestStepDefinition.MaxObjectives, "objectives", where, errors);
        foreach (QuestObjectiveDefinition objective in step.Objectives)
        {
            DataRegistryLoader.CheckRange(objective.Count, 1, MaxStack, "count", where, errors);
            bool found = objective.Type switch
            {
                ObjectiveType.Gather or ObjectiveType.Craft => objective.Target.Length == 0 || known.Items.Contains(objective.Target),
                ObjectiveType.Reach or ObjectiveType.Answer or ObjectiveType.Read => objective.Target.Length == 0,
                ObjectiveType.Talk => known.Npcs.Contains(objective.Target) || objective.Target == AcademyDefinition.OwnKeeper,
                _ => objective.Target.Length == 0 || known.Species.Contains(objective.Target),
            };
            if (!found)
            {
                errors.Add($"{where}: {objective.Type.ToString().ToLowerInvariant()} target '{objective.Target}' is unknown.");
            }

            if (objective.Type == ObjectiveType.Reach && (objective.Radius <= 0 || objective.Radius > MaxRadius || objective.Count != 1))
            {
                errors.Add($"{where}: a reach objective needs a radius of 0–{MaxRadius} m and count 1.");
            }
        }
    }

    // The ids an objective may name.
    private sealed record QuestTargets(HashSet<string> Species, HashSet<string> Items, HashSet<string> Npcs);
}
