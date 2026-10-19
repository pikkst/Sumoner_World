using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Transactions;

namespace SumonerWorld.Tests.Core.Intents;

public class TransactionProcessorTests
{
    [Fact]
    public void A_resent_intent_is_committed_once_and_answered_with_the_first_result()
    {
        var processor = new TransactionProcessor(new IntentDeduplicator());
        var counter = new Counter();
        var transaction = new CountingTransaction(counter, IntentResult.Applied());
        var id = new IntentId(1, 1);

        IntentResult first = processor.Execute(id, ref transaction, 0);
        IntentResult again = processor.Execute(id, ref transaction, 1);

        Assert.Equal(IntentResult.Applied(1), first);
        Assert.Equal(first, again);
        Assert.Equal(1, counter.Commits);
        Assert.Equal((1, 0, 1), (processor.Applied, processor.Rejected, processor.Duplicates));
    }

    [Fact]
    public void A_rejected_intent_changes_nothing_and_stays_rejected_when_resent()
    {
        var processor = new TransactionProcessor(new IntentDeduplicator());
        var counter = new Counter();
        var refused = new CountingTransaction(counter, IntentResult.Reject(IntentRejectReason.OutOfRange));
        var allowed = new CountingTransaction(counter, IntentResult.Applied());
        var id = new IntentId(1, 1);

        processor.Execute(id, ref refused, 0);
        IntentResult resent = processor.Execute(id, ref allowed, 1);

        Assert.Equal(IntentResult.Reject(IntentRejectReason.OutOfRange), resent);
        Assert.Equal(0, counter.Commits);
    }

    [Fact]
    public void A_stale_id_is_rejected_without_validation()
    {
        var processor = new TransactionProcessor(new IntentDeduplicator());
        var counter = new Counter();
        var transaction = new CountingTransaction(counter, IntentResult.Applied());
        processor.Execute(new IntentId(1, 5), ref transaction, 0);

        IntentResult stale = processor.Execute(new IntentId(1, 4), ref transaction, 1);

        Assert.Equal(IntentResult.Reject(IntentRejectReason.Stale), stale);
        Assert.Equal((1, 1), (counter.Validations, counter.Commits));
    }

    private sealed class Counter
    {
        public int Validations { get; set; }

        public int Commits { get; set; }
    }

    private readonly record struct CountingTransaction(Counter Counter, IntentResult Validation) : ITransaction
    {
        public IntentResult Validate(ulong now)
        {
            Counter.Validations++;
            return Validation;
        }

        public IntentResult Commit(ulong now)
        {
            Counter.Commits++;
            return IntentResult.Applied((byte)Counter.Commits);
        }
    }
}
