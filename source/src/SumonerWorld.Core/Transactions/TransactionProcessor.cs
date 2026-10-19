using SumonerWorld.Core.Intents;

namespace SumonerWorld.Core.Transactions;

/// <summary>
/// One atomic change requested by an intent (TDD §3.4). <see cref="Validate"/> authorizes it (owner, range, cooldown, cost) and must
/// not change anything; <see cref="Commit"/> applies all of it and must not fail once validation passed. Implement it as a struct.
/// </summary>
public interface ITransaction
{
    /// <summary>Checks the transaction without changing state; returns <see cref="IntentResult.Applied"/> when it may commit.</summary>
    IntentResult Validate(ulong now);

    /// <summary>Applies the change; returns the applied result (with its detail).</summary>
    IntentResult Commit(ulong now);
}

/// <summary>
/// <b>The only path</b> for atomic multi-entity changes: trades, crafting, Pacts, ownership, rewards (TDD §3.4). Dedupe check →
/// validate → commit → record, so a resent intent returns its first result and is never applied twice.
/// </summary>
public interface ITransactionProcessor
{
    /// <summary>Runs one transaction for one intent.</summary>
    IntentResult Execute<T>(IntentId id, ref T transaction, ulong now)
        where T : struct, ITransaction;
}

/// <summary>
/// In-memory transaction processor (M1-15). From M1-17 the commit and the dedupe record go into the same database transaction, so a
/// crash and resend cannot apply an action twice either.
/// </summary>
public sealed class TransactionProcessor : ITransactionProcessor
{
    private readonly IIntentDeduplicator _dedupe;

    /// <summary>Creates the processor.</summary>
    public TransactionProcessor(IIntentDeduplicator dedupe)
    {
        ArgumentNullException.ThrowIfNull(dedupe);
        _dedupe = dedupe;
    }

    /// <summary>Transactions committed since the start.</summary>
    public int Applied { get; private set; }

    /// <summary>Transactions rejected (validation failed or stale id) since the start.</summary>
    public int Rejected { get; private set; }

    /// <summary>Repeated intent ids answered from the dedupe window since the start.</summary>
    public int Duplicates { get; private set; }

    /// <inheritdoc />
    public IntentResult Execute<T>(IntentId id, ref T transaction, ulong now)
        where T : struct, ITransaction
    {
        switch (_dedupe.Check(id, out IntentResult original))
        {
            case IntentCheck.Duplicate:
                Duplicates++;
                return original;
            case IntentCheck.Stale:
                Rejected++;
                return IntentResult.Reject(IntentRejectReason.Stale);
            default:
                break;
        }

        IntentResult result = transaction.Validate(now);
        if (result.IsApplied)
        {
            result = transaction.Commit(now);
            Applied++;
        }
        else
        {
            Rejected++;
        }

        _dedupe.Record(id, result, now);
        return result;
    }
}
