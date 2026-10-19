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

    /// <summary>The memory event type with this id. Throws <see cref="KeyNotFoundException"/> if unknown.</summary>
    MemoryEventTypeDefinition GetMemoryEventType(ushort id);

    /// <summary>The species with this id. Throws <see cref="KeyNotFoundException"/> if unknown.</summary>
    SpeciesDefinition GetSpecies(string id);
}
