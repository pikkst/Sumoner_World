using SumonerWorld.Core.Data.Definitions;

namespace SumonerWorld.Core.Data;

/// <summary>Items, loot tables and gathering nodes (crafting spec §2, §4, §5, M2-13).</summary>
public static partial class DataRegistryLoader
{
    /// <summary>File kind for <see cref="ItemDefinition"/>.</summary>
    public const string ItemsKind = "items";

    /// <summary>File kind for <see cref="LootTableDefinition"/>.</summary>
    public const string LootTablesKind = "lootTables";

    /// <summary>File kind for <see cref="GatherNodeDefinition"/>.</summary>
    public const string GatherNodesKind = "gatherNodes";

    /// <summary>File kind for <see cref="RecipeDefinition"/> (M2-14).</summary>
    public const string RecipesKind = "recipes";

    private static readonly string[] KnownWearers = ["player", "quadruped", "avian", "serpentine", "biped"];

    private const int MaxStack = 999;
    private const int MaxWeightGrams = 100_000;
    private const int MaxGold = 1_000_000;

    private static void ValidateItem(ItemDefinition item, string where, List<string> errors)
    {
        if (!IsSnakeCaseId(item.Id))
        {
            errors.Add($"{where}: id '{item.Id}' must be snake_case.");
        }

        CheckRange(item.Stack, 1, MaxStack, "stack", where, errors);
        CheckRange(item.WeightGrams, 0, MaxWeightGrams, "weightGrams", where, errors);
        CheckRange(item.GoldValue, 0, MaxGold, "goldValue", where, errors);
        CheckRange(item.Restore.Health, 0, MaxStat, "restore.health", where, errors);
        CheckRange(item.Restore.Stamina, 0, MaxStat, "restore.stamina", where, errors);
        CheckRange(item.Restore.Mana, 0, MaxStat, "restore.mana", where, errors);
        if (item.Slot == EquipSlot.None)
        {
            return;
        }

        if (item.Stack != 1 || item.Wearers.Count == 0 || item.Wearers.Any(wearer => !KnownWearers.Contains(wearer)))
        {
            errors.Add($"{where}: gear stacks 1 and names its wearers ({string.Join(", ", KnownWearers)}).");
        }

        CheckRange(item.MinLevel, 1, Progression.GrowthRules.MaxLevel, "minLevel", where, errors);
        CheckRange(item.Stats.Power, 0, MaxPower, "stats.power", where, errors);
        CheckRange(item.Stats.Guard, 0, MaxDamage, "stats.guard", where, errors);
        CheckRange(item.Stats.ResistPhysical, 0, MaxResist, "stats.resistPhysical", where, errors);
        CheckRange(item.Stats.ResistPrimal, 0, MaxResist, "stats.resistPrimal", where, errors);
        CheckRange(item.Stats.Stamina, 0, MaxStat, "stats.stamina", where, errors);
    }

    private static void ValidateRecipes(List<(RecipeDefinition Entry, string Where)> recipes, HashSet<string> items, List<string> errors)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach ((RecipeDefinition recipe, string where) in recipes)
        {
            if (!IsSnakeCaseId(recipe.Id) || !ids.Add(recipe.Id))
            {
                errors.Add($"{where}: id '{recipe.Id}' must be snake_case and unique.");
            }

            if (recipe.Inputs.Count == 0)
            {
                errors.Add($"{where}: a recipe takes at least one input.");
            }

            foreach (RecipePart part in recipe.Inputs.Append(recipe.Output))
            {
                CheckItem(part.Item, items, where, errors);
                CheckRange(part.Count, 1, MaxStack, "count", where, errors);
            }

            CheckRange(recipe.Ticks, 1, MaxStat, "ticks", where, errors);
        }
    }

    // Loot tables and nodes name known species and items; ids are unique.
    private static void ValidateEconomy(
        List<ItemDefinition> items,
        List<(LootTableDefinition Entry, string Where)> tables,
        List<(GatherNodeDefinition Entry, string Where)> nodes,
        List<(RecipeDefinition Entry, string Where)> recipes,
        List<SpeciesDefinition> species,
        List<string> errors)
    {
        var itemIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (ItemDefinition item in items)
        {
            if (!itemIds.Add(item.Id))
            {
                errors.Add($"Duplicate item id '{item.Id}'.");
            }
        }

        var owners = new HashSet<string>(StringComparer.Ordinal);
        foreach ((LootTableDefinition table, string where) in tables)
        {
            if (!species.Any(definition => definition.Id == table.Species) || !owners.Add(table.Species))
            {
                errors.Add($"{where}: species '{table.Species}' is unknown or already has a loot table.");
            }

            CheckRange(table.GoldMin, 0, MaxGold, "goldMin", where, errors);
            CheckRange(table.GoldMax, table.GoldMin, MaxGold, "goldMax", where, errors);
            foreach (LootEntryDefinition entry in table.Entries)
            {
                CheckItem(entry.Item, itemIds, where, errors);
                CheckRange(entry.ChancePerMille, 0, PerMille, "chancePerMille", where, errors);
                CheckRange(entry.Min, 1, MaxStack, "min", where, errors);
                CheckRange(entry.Max, entry.Min, MaxStack, "max", where, errors);
            }
        }

        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach ((GatherNodeDefinition node, string where) in nodes)
        {
            if (!IsSnakeCaseId(node.Id) || !nodeIds.Add(node.Id))
            {
                errors.Add($"{where}: id '{node.Id}' must be snake_case and unique.");
            }

            CheckItem(node.Item, itemIds, where, errors);
            CheckRange(node.Min, 1, MaxStack, "min", where, errors);
            CheckRange(node.Max, node.Min, MaxStack, "max", where, errors);
            CheckRange(node.RegrowTicks, 1, int.MaxValue, "regrowTicks", where, errors);
        }

        ValidateRecipes(recipes, itemIds, errors);
    }

    private static void CheckItem(string id, HashSet<string> items, string where, List<string> errors)
    {
        if (!items.Contains(id))
        {
            errors.Add($"{where}: unknown item '{id}'.");
        }
    }
}
