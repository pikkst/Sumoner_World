using SumonerWorld.Core.Data.Definitions;

namespace SumonerWorld.Core.Data;

/// <summary>Quests (quests spec, M2-19).</summary>
public static partial class DataRegistryLoader
{
    /// <summary>File kind for <see cref="QuestDefinition"/>.</summary>
    public const string QuestsKind = "quests";

    private const int MaxSteps = 8;
    private const int MaxQuests = 250;
    private const float MaxRadius = 200;

    // Ids, steps, objectives, rewards and the quests required before each (known, and earlier in file order so there are no loops).
    private static void ValidateQuests(
        List<(QuestDefinition Entry, string Where)> quests, List<SpeciesDefinition> species, List<ItemDefinition> items, List<string> errors)
    {
        if (quests.Count > MaxQuests)
        {
            errors.Add($"{quests.Count} quests; at most {MaxQuests}.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var itemIds = items.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var speciesIds = species.Select(definition => definition.Id).ToHashSet(StringComparer.Ordinal);
        foreach ((QuestDefinition quest, string where) in quests)
        {
            foreach (string required in quest.Requires.Where(required => !ids.Contains(required)))
            {
                errors.Add($"{where}: requires '{required}', which is not a quest earlier in the files.");
            }

            if (!IsSnakeCaseId(quest.Id) || !ids.Add(quest.Id))
            {
                errors.Add($"{where}: id '{quest.Id}' must be snake_case and unique.");
            }

            CheckRange(quest.MinLevel, 1, Progression.GrowthRules.MaxLevel, "minLevel", where, errors);
            CheckRange(quest.Steps.Count, 1, MaxSteps, "steps", where, errors);
            for (int s = 0; s < quest.Steps.Count; s++)
            {
                ValidateStep(quest.Steps[s], $"{where}.steps[{s}]", speciesIds, itemIds, errors);
            }

            CheckRange(quest.Rewards.Xp, 0, MaxGold, "rewards.xp", where, errors);
            CheckRange(quest.Rewards.Gold, 0, MaxGold, "rewards.gold", where, errors);
            foreach (RecipePart part in quest.Rewards.Items)
            {
                CheckItem(part.Item, itemIds, where, errors);
                CheckRange(part.Count, 1, MaxStack, "rewards.items.count", where, errors);
            }
        }
    }

    private static void ValidateStep(QuestStepDefinition step, string where, HashSet<string> species, HashSet<string> items, List<string> errors)
    {
        CheckRange(step.Objectives.Count, 1, QuestStepDefinition.MaxObjectives, "objectives", where, errors);
        foreach (QuestObjectiveDefinition objective in step.Objectives)
        {
            CheckRange(objective.Count, 1, MaxStack, "count", where, errors);
            bool known = objective.Type switch
            {
                ObjectiveType.Gather or ObjectiveType.Craft => objective.Target.Length == 0 || items.Contains(objective.Target),
                ObjectiveType.Reach => objective.Target.Length == 0,
                _ => objective.Target.Length == 0 || species.Contains(objective.Target),
            };
            if (!known)
            {
                errors.Add($"{where}: {objective.Type.ToString().ToLowerInvariant()} target '{objective.Target}' is unknown.");
            }

            if (objective.Type == ObjectiveType.Reach && (objective.Radius <= 0 || objective.Radius > MaxRadius || objective.Count != 1))
            {
                errors.Add($"{where}: a reach objective needs a radius of 0–{MaxRadius} m and count 1.");
            }
        }
    }
}
