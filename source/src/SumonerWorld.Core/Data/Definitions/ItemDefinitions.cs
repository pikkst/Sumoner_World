namespace SumonerWorld.Core.Data.Definitions;

/// <summary>Item categories (crafting spec §2).</summary>
public enum ItemCategory
{
    /// <summary>Crafting material.</summary>
    Material,

    /// <summary>Food.</summary>
    Food,

    /// <summary>A Pact offering.</summary>
    Offering,

    /// <summary>A weapon.</summary>
    Weapon,

    /// <summary>Armour.</summary>
    Armour,

    /// <summary>A charm.</summary>
    Charm,

    /// <summary>A tool.</summary>
    Tool,

    /// <summary>A quest item (soulbound).</summary>
    Quest,

    /// <summary>A boss egg (soulbound until hatched).</summary>
    Egg,
}

/// <summary>An item (crafting spec §2), from <c>data/items/items.json</c>.</summary>
public sealed record ItemDefinition
{
    /// <summary>Stable id in snake_case, e.g. <c>fox_fur</c>.</summary>
    public required string Id { get; init; }

    /// <summary>Category.</summary>
    public required ItemCategory Category { get; init; }

    /// <summary>Most in one inventory slot.</summary>
    public int Stack { get; init; } = 20;

    /// <summary>Weight of one, in grams.</summary>
    public int WeightGrams { get; init; } = 100;

    /// <summary>What a shop pays for one, in gold.</summary>
    public int GoldValue { get; init; }

    /// <summary>Tags (e.g. <c>fur</c>, <c>plant</c>).</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];
}

/// <summary>What a defeated creature of a species drops (crafting spec §4), from <c>data/items/loot_tables.json</c>.</summary>
public sealed record LootTableDefinition
{
    /// <summary>The species id.</summary>
    public required string Species { get; init; }

    /// <summary>Least gold.</summary>
    public int GoldMin { get; init; }

    /// <summary>Most gold.</summary>
    public int GoldMax { get; init; }

    /// <summary>Item rolls, each on its own.</summary>
    public IReadOnlyList<LootEntryDefinition> Entries { get; init; } = [];
}

/// <summary>One roll of a loot table: the item drops with <see cref="ChancePerMille"/>, between <see cref="Min"/> and <see cref="Max"/> of it.</summary>
public sealed record LootEntryDefinition
{
    /// <summary>The item id.</summary>
    public required string Item { get; init; }

    /// <summary>Chance per mille (0–1000).</summary>
    public required int ChancePerMille { get; init; }

    /// <summary>Least count when it drops.</summary>
    public int Min { get; init; } = 1;

    /// <summary>Most count when it drops.</summary>
    public int Max { get; init; } = 1;
}

/// <summary>A kind of gathering node (crafting spec §5), from <c>data/items/gather_nodes.json</c>.</summary>
public sealed record GatherNodeDefinition
{
    /// <summary>Stable id, e.g. <c>berry_bush</c>.</summary>
    public required string Id { get; init; }

    /// <summary>The item it gives.</summary>
    public required string Item { get; init; }

    /// <summary>Least it gives.</summary>
    public int Min { get; init; } = 1;

    /// <summary>Most it gives.</summary>
    public int Max { get; init; } = 1;

    /// <summary>Ticks until it can be gathered again (an in-game day for plants).</summary>
    public required int RegrowTicks { get; init; }
}
