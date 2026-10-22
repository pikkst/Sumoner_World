using SumonerWorld.Core.Data.Definitions;

namespace SumonerWorld.Core.Data;

/// <summary>
/// Read-only, validated game data (R08). Built once at start-up by <see cref="DataRegistryLoader"/>; lookups do not allocate.
/// Lists are ordered by id so iteration is deterministic (R11).
/// </summary>
public interface IDataRegistry
{
    /// <summary>All memory event types, ordered by id.</summary>
    IReadOnlyList<MemoryEventTypeDefinition> MemoryEventTypes { get; }

    /// <summary>All species, ordered by id.</summary>
    IReadOnlyList<SpeciesDefinition> Species { get; }

    /// <summary>Opinion weights per memory event type, ordered by event type (AI Bible §5).</summary>
    IReadOnlyList<OpinionWeightsDefinition> OpinionWeights { get; }

    /// <summary>Utility AI goals, ordered by id (AI Bible §7).</summary>
    IReadOnlyList<GoalDefinition> Goals { get; }

    /// <summary>One behaviour tree per goal, ordered by goal key (AI Bible §7.3).</summary>
    IReadOnlyList<BehaviourTreeDefinition> BehaviourTrees { get; }

    /// <summary>The Pact Term catalogue in file order; a term's index is its bit in saved Pacts (AI Bible §10.2).</summary>
    IReadOnlyList<PactTermDefinition> PactTerms { get; }

    /// <summary>Bond levels 0…N (AI Bible §10.3).</summary>
    IReadOnlyList<BondLevelDefinition> BondLevels { get; }

    /// <summary>All moves, ordered by id (combat spec §3); a move's index is what <c>ActiveMove</c> holds.</summary>
    IReadOnlyList<MoveDefinition> Moves { get; }

    /// <summary>Combat rules and the player's base stats (combat spec §2, §5, §6).</summary>
    CombatDefinition Combat { get; }

    /// <summary>Skill trees, ordered by species id (companions spec §5).</summary>
    IReadOnlyList<SkillTreeDefinition> SkillTrees { get; }

    /// <summary>Spells (runes composed into moves of the same id), ordered by id (combat spec §8).</summary>
    IReadOnlyList<SpellDefinition> Spells { get; }

    /// <summary>Status effects, ordered by id (combat spec §7).</summary>
    IReadOnlyList<EffectDefinition> Effects { get; }

    /// <summary>Combos in file order; a combo's index is its saved bit (combat spec §10).</summary>
    IReadOnlyList<ComboDefinition> Combos { get; }

    /// <summary>Items, ordered by id (crafting spec §2); an item's index is what inventories hold.</summary>
    IReadOnlyList<ItemDefinition> Items { get; }

    /// <summary>Gathering node kinds, ordered by id (crafting spec §5).</summary>
    IReadOnlyList<GatherNodeDefinition> GatherNodes { get; }

    /// <summary>Recipes in file order (crafting spec §6); a recipe's index is what the client names it by.</summary>
    IReadOnlyList<RecipeDefinition> Recipes { get; }

    /// <summary>Quests in file order (quests spec, M2-19); a quest's index is what the client names it by.</summary>
    IReadOnlyList<QuestDefinition> Quests { get; }

    /// <summary>The Summoner Academy (M2-21), or null when the data has none.</summary>
    AcademyDefinition? Academy { get; }

    /// <summary>Index of the item with this id in <see cref="Items"/>; throws when unknown.</summary>
    int ItemIndex(string id);

    /// <summary>The loot table of the species at this index, or null.</summary>
    LootTableDefinition? LootTableOf(int species);

    /// <summary>The effect with this id, or null.</summary>
    EffectDefinition? EffectOf(string id);

    /// <summary>The skill tree of the species at this index in <see cref="Species"/>, or null when it has none.</summary>
    SkillTreeDefinition? SkillTreeOf(int species);

    /// <summary>Index of the move with this id in <see cref="Moves"/>. Throws <see cref="KeyNotFoundException"/> if unknown.</summary>
    int MoveIndex(string id);

    /// <summary>The memory event type with this id. Throws <see cref="KeyNotFoundException"/> if unknown.</summary>
    MemoryEventTypeDefinition GetMemoryEventType(ushort id);

    /// <summary>The species with this id. Throws <see cref="KeyNotFoundException"/> if unknown.</summary>
    SpeciesDefinition GetSpecies(string id);
}
