using SumonerWorld.Core.Data.Definitions;

namespace SumonerWorld.Core.Data;

/// <summary>Immutable <see cref="IDataRegistry"/>. Create it with <see cref="DataRegistryLoader.Load"/>.</summary>
public sealed class DataRegistry : IDataRegistry
{
    private readonly Dictionary<ushort, MemoryEventTypeDefinition> _memoryEventTypesById;
    private readonly Dictionary<string, SpeciesDefinition> _speciesById;

    internal DataRegistry(
        IReadOnlyList<MemoryEventTypeDefinition> memoryEventTypes,
        IReadOnlyList<SpeciesDefinition> species,
        IReadOnlyList<OpinionWeightsDefinition> opinionWeights,
        IReadOnlyList<GoalDefinition> goals,
        IReadOnlyList<BehaviourTreeDefinition> behaviourTrees,
        IReadOnlyList<PactTermDefinition> pactTerms,
        IReadOnlyList<BondLevelDefinition> bondLevels)
    {
        PactTerms = pactTerms;
        BondLevels = bondLevels;
        Goals = goals;
        BehaviourTrees = behaviourTrees;
        MemoryEventTypes = memoryEventTypes;
        Species = species;
        OpinionWeights = opinionWeights;
        _memoryEventTypesById = memoryEventTypes.ToDictionary(definition => definition.Id);
        _speciesById = species.ToDictionary(definition => definition.Id, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public IReadOnlyList<MemoryEventTypeDefinition> MemoryEventTypes { get; }

    /// <inheritdoc />
    public IReadOnlyList<SpeciesDefinition> Species { get; }

    /// <inheritdoc />
    public IReadOnlyList<OpinionWeightsDefinition> OpinionWeights { get; }

    /// <inheritdoc />
    public IReadOnlyList<GoalDefinition> Goals { get; }

    /// <inheritdoc />
    public IReadOnlyList<BehaviourTreeDefinition> BehaviourTrees { get; }

    /// <inheritdoc />
    public IReadOnlyList<PactTermDefinition> PactTerms { get; }

    /// <inheritdoc />
    public IReadOnlyList<BondLevelDefinition> BondLevels { get; }

    /// <inheritdoc />
    public MemoryEventTypeDefinition GetMemoryEventType(ushort id) =>
        _memoryEventTypesById.TryGetValue(id, out MemoryEventTypeDefinition? definition)
            ? definition
            : throw new KeyNotFoundException($"Unknown memory event type id {id}.");

    /// <inheritdoc />
    public SpeciesDefinition GetSpecies(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _speciesById.TryGetValue(id, out SpeciesDefinition? definition)
            ? definition
            : throw new KeyNotFoundException($"Unknown species id '{id}'.");
    }
}
