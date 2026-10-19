using SumonerWorld.Core.Data.Definitions;

namespace SumonerWorld.Core.Cognition.Memory;

/// <summary>
/// Memory event types prepared for fast lookup by id (array, no hashing), with half-lives converted to ticks.
/// Built once at start-up from the data registry and the simulation settings (R08, R48).
/// </summary>
public sealed class MemoryRules
{
    private readonly MemoryEventTypeDefinition?[] _byId;
    private readonly uint[] _halfLifeTicksById;
    private readonly int[] _subjectMaskById;

    /// <summary>Builds the rules from event type definitions and the in-game day length in ticks.</summary>
    public MemoryRules(IEnumerable<MemoryEventTypeDefinition> eventTypes, int ticksPerDay)
    {
        ArgumentNullException.ThrowIfNull(eventTypes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ticksPerDay);

        MemoryEventTypeDefinition[] definitions = [.. eventTypes];
        int size = definitions.Length == 0 ? 1 : definitions.Max(definition => definition.Id) + 1;
        _byId = new MemoryEventTypeDefinition?[size];
        _halfLifeTicksById = new uint[size];
        _subjectMaskById = new int[size];
        foreach (MemoryEventTypeDefinition definition in definitions)
        {
            _byId[definition.Id] = definition;
            _halfLifeTicksById[definition.Id] = definition.HalfLifeDays is int days ? checked((uint)days * (uint)ticksPerDay) : 0;
            foreach (SubjectKind kind in definition.Subjects)
            {
                _subjectMaskById[definition.Id] |= 1 << (int)kind;
            }
        }
    }

    /// <summary>The definition of an event type. Throws for unknown ids (a code or data bug).</summary>
    public MemoryEventTypeDefinition Get(ushort eventType) =>
        eventType < _byId.Length && _byId[eventType] is MemoryEventTypeDefinition definition
            ? definition
            : throw new ArgumentOutOfRangeException(nameof(eventType), eventType, "Unknown memory event type.");

    /// <summary>True if memories of this event type may be about <paramref name="kind"/> (precomputed, no allocation).</summary>
    public bool AllowsSubject(ushort eventType, SubjectKind kind)
    {
        _ = Get(eventType);
        return (_subjectMaskById[eventType] & (1 << (int)kind)) != 0;
    }

    /// <summary>Half-life of an event type in ticks; 0 means it never decays.</summary>
    public uint HalfLifeTicks(ushort eventType)
    {
        _ = Get(eventType);
        return _halfLifeTicksById[eventType];
    }
}
