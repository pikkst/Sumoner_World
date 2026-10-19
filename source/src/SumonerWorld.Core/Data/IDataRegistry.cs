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

    /// <summary>The memory event type with this id. Throws <see cref="KeyNotFoundException"/> if unknown.</summary>
    MemoryEventTypeDefinition GetMemoryEventType(ushort id);

    /// <summary>The species with this id. Throws <see cref="KeyNotFoundException"/> if unknown.</summary>
    SpeciesDefinition GetSpecies(string id);
}
