using SumonerWorld.Core.Data.Definitions;

namespace SumonerWorld.Core.Cognition.Memory;

/// <summary>Handle to one creature's mind in an <see cref="ICreatureMindStore"/>; stale handles are rejected.</summary>
/// <param name="Slot">Slot in the store.</param>
/// <param name="Generation">Incremented every time the slot is released.</param>
public readonly record struct MindHandle(int Slot, int Generation);

/// <summary>What <see cref="ICreatureMindStore.Remember"/> did.</summary>
public enum MemoryOutcome : byte
{
    /// <summary>A new episodic memory was stored.</summary>
    Created,

    /// <summary>An existing memory of the same event and subject was strengthened.</summary>
    Reinforced,

    /// <summary>A new core memory took a core slot (personality drift applied; the creature should react visibly).</summary>
    CoreFormed,
}

/// <summary>Result of <see cref="ICreatureMindStore.Remember"/>.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="EvictedAnother">True if a weaker memory was dropped to make room.</param>
/// <param name="DriftedTrait">The trait that shifted, if a core memory with drift formed.</param>
public readonly record struct RememberResult(MemoryOutcome Outcome, bool EvictedAnother, PersonalityTrait? DriftedTrait);

/// <summary>
/// Creatures' personalities and memories (AI Bible §3–4). Fixed capacity per creature: <see cref="CoreCapacity"/> core slots
/// and <see cref="EpisodicCapacity"/> episodic slots. Decay is lazy: nothing happens per tick; strength is computed on recall.
/// Server-only; never replicated (AI Bible §1).
/// </summary>
public interface ICreatureMindStore
{
    /// <summary>Core (pinned) slots per creature (AI Bible §4.4).</summary>
    public const int CoreCapacity = 8;

    /// <summary>Episodic slots per creature (AI Bible §4.4).</summary>
    public const int EpisodicCapacity = 48;

    /// <summary>Below this strength an unpinned memory is forgotten: not recalled and free to be replaced (AI Bible §4.3).</summary>
    public const int ForgetThreshold = 20;

    /// <summary>Pinned core memories never decay below this strength (AI Bible §4.3).</summary>
    public const int PinnedFloor = 300;

    /// <summary>Highest strength a memory can have.</summary>
    public const int MaxSalience = 1000;

    /// <summary>Most minds the store can hold.</summary>
    int Capacity { get; }

    /// <summary>Minds currently allocated.</summary>
    int Count { get; }

    /// <summary>Creates an empty mind with the given birth personality.</summary>
    MindHandle Allocate(Personality birth);

    /// <summary>Frees a mind and all its memories.</summary>
    void Release(MindHandle mind);

    /// <summary>True if the handle refers to a live mind.</summary>
    bool IsValid(MindHandle mind);

    /// <summary>Personality at birth.</summary>
    Personality GetBirthPersonality(MindHandle mind);

    /// <summary>Current personality: birth plus drift from core memories.</summary>
    Personality GetPersonality(MindHandle mind);

    /// <summary>Records an episode at tick <paramref name="now"/>, merging it with a still-remembered memory of the same event and subject.</summary>
    RememberResult Remember(MindHandle mind, ushort eventType, MemorySubject subject, ulong now);

    /// <summary>
    /// Writes the strongest remembered memories into <paramref name="results"/>, strongest first (ties: oldest first), and returns how
    /// many were written. Forgotten memories are skipped.
    /// </summary>
    int Recall(MindHandle mind, ulong now, Span<RecalledMemory> results);

    /// <summary>Like <see cref="Recall"/>, but only memories about <paramref name="subject"/>.</summary>
    int RecallAbout(MindHandle mind, MemorySubject subject, ulong now, Span<RecalledMemory> results);

    /// <summary>Core-slot records as stored (for saves and the AI Inspector).</summary>
    ReadOnlySpan<MemoryRecord> GetCoreRecords(MindHandle mind);

    /// <summary>Episodic records as stored, including forgotten ones not yet replaced (for saves and the AI Inspector).</summary>
    ReadOnlySpan<MemoryRecord> GetEpisodicRecords(MindHandle mind);
}
