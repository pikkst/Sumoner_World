using SumonerWorld.Core.Intents;

namespace SumonerWorld.Tests.Core.Intents;

public class IntentDeduplicatorTests
{
    private static readonly IntentResult Done = IntentResult.Applied(7);

    [Fact]
    public void A_repeated_id_returns_the_first_result()
    {
        var dedupe = new IntentDeduplicator();
        var id = new IntentId(1, 1);

        Assert.Equal(IntentCheck.New, dedupe.Check(id, out _));
        dedupe.Record(id, Done, now: 10);

        Assert.Equal(IntentCheck.Duplicate, dedupe.Check(id, out IntentResult original));
        Assert.Equal(Done, original);
        Assert.Equal(IntentCheck.New, dedupe.Check(new IntentId(1, 2), out _));
    }

    [Fact]
    public void Lower_unseen_ids_and_ids_older_than_the_window_are_stale_never_new()
    {
        var dedupe = new IntentDeduplicator();
        dedupe.Record(new IntentId(1, 5), Done, 0);
        Assert.Equal(IntentCheck.Stale, dedupe.Check(new IntentId(1, 3), out _));

        for (uint sequence = 6; sequence < 6 + IntentDeduplicator.Window + 10; sequence++)
        {
            dedupe.Record(new IntentId(1, sequence), Done, sequence);
        }

        Assert.Equal(IntentCheck.Stale, dedupe.Check(new IntentId(1, 5), out _));
        Assert.Equal(IntentCheck.Duplicate, dedupe.Check(new IntentId(1, 6 + IntentDeduplicator.Window), out _));
    }

    [Fact]
    public void Sessions_are_independent_and_recording_an_old_id_is_a_bug()
    {
        var dedupe = new IntentDeduplicator();
        dedupe.Record(new IntentId(1, 9), Done, 0);

        Assert.Equal(IntentCheck.New, dedupe.Check(new IntentId(2, 9), out _));
        Assert.Throws<InvalidOperationException>(() => dedupe.Record(new IntentId(1, 9), Done, 0));
    }

    [Fact]
    public void Idle_sessions_are_pruned_after_ten_minutes()
    {
        var dedupe = new IntentDeduplicator();
        dedupe.Record(new IntentId(1, 1), Done, 0);
        dedupe.Record(new IntentId(2, 1), Done, 10_000);

        Assert.Equal(0, dedupe.Prune(IntentDeduplicator.MaxIdleTicks));
        Assert.Equal(1, dedupe.Prune(IntentDeduplicator.MaxIdleTicks + 1));
        Assert.Equal(1, dedupe.Sessions);

        dedupe.Forget(2);
        Assert.Equal(0, dedupe.Sessions);
    }
}
