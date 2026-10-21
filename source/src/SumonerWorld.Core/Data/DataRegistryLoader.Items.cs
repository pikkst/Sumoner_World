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
    }

    // Loot tables and nodes name known species and items; ids are unique.
    private static void ValidateEconomy(
        List<ItemDefinition> items, List<(LootTableDefinition Entry, string Where)> tables, List<(GatherNodeDefinition Entry, string Where)> nodes,
        List<SpeciesDefinition> species, List<string> errors)
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
    }

    private static void CheckItem(string id, HashSet<string> items, string where, List<string> errors)
    {
        if (!items.Contains(id))
        {
            errors.Add($"{where}: unknown item '{id}'.");
        }
    }
}
