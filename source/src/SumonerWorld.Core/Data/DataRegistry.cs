using SumonerWorld.Core.Data.Definitions;

namespace SumonerWorld.Core.Data;

/// <summary>Immutable <see cref="IDataRegistry"/>. Create it with <see cref="DataRegistryLoader.Load"/>.</summary>
public sealed class DataRegistry : IDataRegistry
{
    private readonly Dictionary<ushort, MemoryEventTypeDefinition> _memoryEventTypesById;
    private readonly Dictionary<string, SpeciesDefinition> _speciesById;
    private readonly Dictionary<string, int> _moveIndexById;
    private readonly SkillTreeDefinition?[] _treeBySpecies;

    internal DataRegistry(
        IReadOnlyList<MemoryEventTypeDefinition> memoryEventTypes,
        IReadOnlyList<SpeciesDefinition> species,
        IReadOnlyList<OpinionWeightsDefinition> opinionWeights,
        IReadOnlyList<GoalDefinition> goals,
        IReadOnlyList<BehaviourTreeDefinition> behaviourTrees,
        IReadOnlyList<PactTermDefinition> pactTerms,
        IReadOnlyList<BondLevelDefinition> bondLevels,
        IReadOnlyList<MoveDefinition> moves,
        CombatDefinition combat,
        IReadOnlyList<SkillTreeDefinition> skillTrees)
    {
        SkillTrees = skillTrees;
        _treeBySpecies = [.. species.Select(definition => skillTrees.FirstOrDefault(tree => tree.Species == definition.Id))];
        Moves = moves;
        Combat = combat;
        _moveIndexById = moves.Select((definition, index) => (definition.Id, index)).ToDictionary(item => item.Id, item => item.index, StringComparer.Ordinal);
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
    public IReadOnlyList<MoveDefinition> Moves { get; }

    /// <inheritdoc />
    public CombatDefinition Combat { get; }

    /// <inheritdoc />
    public IReadOnlyList<SkillTreeDefinition> SkillTrees { get; }

    /// <inheritdoc />
    public SkillTreeDefinition? SkillTreeOf(int species) => (uint)species < (uint)_treeBySpecies.Length ? _treeBySpecies[species] : null;

    /// <inheritdoc />
    public int MoveIndex(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _moveIndexById.TryGetValue(id, out int index) ? index : throw new KeyNotFoundException($"Unknown move id '{id}'.");
    }

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
