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

    /// <summary>Gear: the kind of slot it is worn in (<see cref="EquipSlot.None"/> for other items, crafting spec §8).</summary>
    public EquipSlot Slot { get; init; } = EquipSlot.None;

    /// <summary>Gear: who can wear it: <c>player</c> and/or body types (<c>quadruped</c>, <c>avian</c>, …).</summary>
    public IReadOnlyList<string> Wearers { get; init; } = [];

    /// <summary>Gear: the lowest level of its wearer.</summary>
    public int MinLevel { get; init; } = 1;

    /// <summary>Gear: what it adds to its wearer's combat stats.</summary>
    public GearStats Stats { get; init; } = new();
}

/// <summary>The kinds of equipment slot (crafting spec §8).</summary>
public enum EquipSlot
{
    /// <summary>Not gear.</summary>
    None,

    /// <summary>A weapon.</summary>
    Weapon,

    /// <summary>The off hand.</summary>
    OffHand,

    /// <summary>Head.</summary>
    Head,

    /// <summary>Body.</summary>
    Body,

    /// <summary>A charm (players wear two).</summary>
    Charm,
}

/// <summary>What gear adds to combat stats (combat spec §2).</summary>
public sealed record GearStats
{
    /// <summary>Power, in percent.</summary>
    public int Power { get; init; }

    /// <summary>Guard, flat.</summary>
    public int Guard { get; init; }

    /// <summary>Physical resistance, per mille.</summary>
    public int ResistPhysical { get; init; }

    /// <summary>Primal resistance, per mille.</summary>
    public int ResistPrimal { get; init; }

    /// <summary>Stamina, flat.</summary>
    public int Stamina { get; init; }
}

/// <summary>Where a recipe is made (crafting spec §6). The slice has no stations yet: every slice recipe is made by hand.</summary>
public enum CraftStation
{
    /// <summary>Anywhere.</summary>
    Hand,

    /// <summary>At a campfire.</summary>
    Campfire,

    /// <summary>At a workbench.</summary>
    Workbench,

    /// <summary>At a forge.</summary>
    Forge,
}

/// <summary>A recipe (crafting spec §6), from <c>data/items/recipes.json</c>. Its position in the file is its index on the wire.</summary>
public sealed record RecipeDefinition
{
    /// <summary>Stable id, e.g. <c>wooden_staff</c>.</summary>
    public required string Id { get; init; }

    /// <summary>What it takes (all taken when it is done).</summary>
    public required IReadOnlyList<RecipePart> Inputs { get; init; }

    /// <summary>What it makes.</summary>
    public required RecipePart Output { get; init; }

    /// <summary>Where it is made.</summary>
    public CraftStation Station { get; init; } = CraftStation.Hand;

    /// <summary>How long it takes, in ticks.</summary>
    public required int Ticks { get; init; }
}

/// <summary>An item and a count in a recipe.</summary>
public sealed record RecipePart
{
    /// <summary>Item id.</summary>
    public required string Item { get; init; }

    /// <summary>How many.</summary>
    public int Count { get; init; } = 1;
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
