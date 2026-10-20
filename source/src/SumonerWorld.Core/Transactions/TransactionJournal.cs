using SumonerWorld.Core.Intents;
using SumonerWorld.Core.World;

namespace SumonerWorld.Core.Transactions;

/// <summary>An intent's outcome to make durable before its result is sent (TDD §4.2).</summary>
/// <param name="Id">The intent.</param>
/// <param name="Result">Its result.</param>
/// <param name="Tick">When it was processed.</param>
/// <param name="IsNew">False for a repeat answered from the dedupe window (nothing new to record, it only waits for the original).</param>
/// <param name="Changed">Creatures the transaction changed, as they are now.</param>
public sealed record JournalEntry(IntentId Id, IntentResult Result, ulong Tick, bool IsNew, IReadOnlyList<CreatureSave> Changed);

/// <summary>
/// The transaction journal (TDD §4.2): the simulation appends every processed intent and carries on; the persistence thread writes
/// each entry's changes and its dedupe record in one database transaction, in append order. The intent's result may be sent only
/// once <see cref="TryTakeDurable"/> has returned it, so an acknowledged action survives a crash.
/// </summary>
public interface ITransactionJournal
{
    /// <summary>Queues an entry; returns at once (simulation thread).</summary>
    void Append(JournalEntry entry);

    /// <summary>The next entry that is now durable, in append order (simulation thread).</summary>
    bool TryTakeDurable(out IntentId id);
}
