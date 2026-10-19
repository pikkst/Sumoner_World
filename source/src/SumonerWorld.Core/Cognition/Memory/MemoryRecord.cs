using System.Runtime.InteropServices;
using SumonerWorld.Core.Data.Definitions;

namespace SumonerWorld.Core.Cognition.Memory;

/// <summary>Tags on a memory record (AI Bible §4.1 <c>Flags</c>).</summary>
[Flags]
public enum MemoryTags : byte
{
    /// <summary>No tags.</summary>
    None = 0,

    /// <summary>The event type is a core type.</summary>
    Core = 1,

    /// <summary>Heard from another group member, not experienced (AI Bible §4.5).</summary>
    Rumour = 2,

    /// <summary>Seen happening to someone else.</summary>
    Witnessed = 4,

    /// <summary>Held in one of the core slots: never evicted, never decays below the floor.</summary>
    Pinned = 8,
}

/// <summary>Who or what a memory is about: a player account, creature, species, place or item type (AI Bible §4.1).</summary>
/// <param name="Kind">What kind of thing the id refers to.</param>
/// <param name="Id">Account id, creature <c>StableId</c>, species index, place cell or item type id. Never a display name.</param>
public readonly record struct MemorySubject(SubjectKind Kind, ulong Id);

/// <summary>
/// One remembered episode (AI Bible §4.1). Integers only, so decay, saves and replays are bit-exact (R11). 32 bytes.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct MemoryRecord
{
    /// <summary>Creates a record.</summary>
    public MemoryRecord(
        ushort eventType, MemorySubject subject, sbyte valence, ushort salience, uint createdTick, uint lastReinforcedTick, ushort count, MemoryTags tags)
    {
        SubjectId = subject.Id;
        CreatedTick = createdTick;
        LastReinforcedTick = lastReinforcedTick;
        EventType = eventType;
        Salience = salience;
        Count = count;
        SubjectKind = subject.Kind;
        Valence = valence;
        Tags = tags;
    }

    /// <summary>Subject id (see <see cref="Subject"/>).</summary>
    public ulong SubjectId { get; init; }

    /// <summary>Tick the memory first formed.</summary>
    public uint CreatedTick { get; init; }

    /// <summary>Tick <see cref="Salience"/> was last set; decay is measured from here.</summary>
    public uint LastReinforcedTick { get; init; }

    /// <summary>Memory event type id (data).</summary>
    public ushort EventType { get; init; }

    /// <summary>Strength at <see cref="LastReinforcedTick"/>, in [0, 1000].</summary>
    public ushort Salience { get; init; }

    /// <summary>How many episodes were merged into this record.</summary>
    public ushort Count { get; init; }

    /// <summary>Subject kind (see <see cref="Subject"/>).</summary>
    public SubjectKind SubjectKind { get; init; }

    /// <summary>How good (+) or bad (−) the episode was, in [−100, 100].</summary>
    public sbyte Valence { get; init; }

    /// <summary>Record tags.</summary>
    public MemoryTags Tags { get; init; }

    /// <summary>Who or what the memory is about.</summary>
    public MemorySubject Subject => new(SubjectKind, SubjectId);

    /// <summary>True if the record sits in a core slot.</summary>
    public bool IsPinned => (Tags & MemoryTags.Pinned) != 0;
}

/// <summary>A memory as recalled at a given tick, with its decayed strength.</summary>
/// <param name="Record">The stored record.</param>
/// <param name="EffectiveSalience">Strength now, after decay, in [0, 1000].</param>
public readonly record struct RecalledMemory(MemoryRecord Record, int EffectiveSalience);
