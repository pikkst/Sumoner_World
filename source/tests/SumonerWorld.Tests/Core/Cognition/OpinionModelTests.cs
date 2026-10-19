using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Cognition.Opinions;
using static SumonerWorld.Tests.Core.Cognition.CognitionTestData;

namespace SumonerWorld.Tests.Core.Cognition;

public class OpinionModelTests
{
    private static readonly Personality Calm = new(0, 0, 0, 0);

    [Fact]
    public void A_stranger_is_neutral_and_unknown()
    {
        var (store, mind) = StoreWithMind(Calm);
        var opinions = new OpinionModel(store, ShippedRules());

        Assert.Equal(Opinion.Neutral, opinions.GetOpinion(mind, Alice, 0));
        Assert.False(opinions.Knows(mind, Alice, 0));
    }

    [Fact]
    public void Feeding_builds_trust_and_affection_and_lowers_fear()
    {
        var (store, mind) = StoreWithMind(Calm);
        var opinions = new OpinionModel(store, ShippedRules());

        store.Remember(mind, FedBy, Alice, 0);
        Opinion once = opinions.GetOpinion(mind, Alice, 0);
        for (int day = 1; day < 10; day++)
        {
            store.Remember(mind, FedBy, Alice, (ulong)day * TicksPerDay);
        }

        Opinion daily = opinions.GetOpinion(mind, Alice, 9UL * TicksPerDay);

        Assert.True(once.Trust > 0 && once.Affection > 0 && once.Fear <= 0);
        Assert.True(daily.Trust > once.Trust, "Ten days of feeding must build more trust than one meal.");
        Assert.True(opinions.Knows(mind, Alice, 9UL * TicksPerDay));
    }

    [Fact]
    public void Being_attacked_creates_fear_and_distrust_toward_the_attacker_only()
    {
        var (store, mind) = StoreWithMind(Calm);
        var opinions = new OpinionModel(store, ShippedRules());

        store.Remember(mind, AttackedBy, Alice, 0);
        Opinion ofAlice = opinions.GetOpinion(mind, Alice, 0);

        Assert.True(ofAlice.Fear > 0);
        Assert.True(ofAlice.Trust < 0);
        Assert.Equal(Opinion.Neutral, opinions.GetOpinion(mind, Bob, 0));
    }

    [Fact]
    public void Opinions_fade_with_memories_but_a_long_term_baseline_remains()
    {
        var (store, mind) = StoreWithMind(Calm);
        var opinions = new OpinionModel(store, ShippedRules());
        for (int i = 0; i < 5; i++)
        {
            store.Remember(mind, AttackedBy, Alice, (ulong)i);
        }

        Opinion fresh = opinions.GetOpinion(mind, Alice, 5);
        Opinion yearLater = opinions.GetOpinion(mind, Alice, 400UL * TicksPerDay);

        Assert.True(yearLater.Fear < fresh.Fear, "Fear fades as the memory fades.");
        Assert.True(yearLater.Fear > 0, "The baseline keeps a trace of repeated attacks after the memory is forgotten.");
        Assert.Equal(yearLater, store.GetOpinionBaseline(mind, Alice));
    }

    [Fact]
    public void A_saved_life_outweighs_an_attack()
    {
        var (store, mind) = StoreWithMind(Calm);
        var opinions = new OpinionModel(store, ShippedRules());

        store.Remember(mind, AttackedBy, Alice, 0);
        store.Remember(mind, SavedLifeOf, Alice, 1);

        Assert.True(opinions.GetOpinion(mind, Alice, 1).Trust > 0);
    }

    [Fact]
    public void Attitudes_and_baselines_stay_within_their_limits()
    {
        var (store, mind) = StoreWithMind(Calm);
        var opinions = new OpinionModel(store, ShippedRules());

        for (int i = 0; i < 200; i++)
        {
            store.Remember(mind, KilledKin, Alice, (ulong)i);
            store.Remember(mind, AttackedBy, Alice, (ulong)i);
        }

        Opinion opinion = opinions.GetOpinion(mind, Alice, 200);
        Opinion baseline = store.GetOpinionBaseline(mind, Alice);
        Assert.Equal(Opinion.Min, opinion.Trust);
        Assert.InRange(baseline.Trust, -ICreatureMindStore.MaxBaseline, ICreatureMindStore.MaxBaseline);
        Assert.Equal(-ICreatureMindStore.MaxBaseline, baseline.Trust);
    }

    [Fact]
    public void Only_the_24_strongest_baselines_are_kept()
    {
        var (store, mind) = StoreWithMind(Calm);
        var strong = new MemorySubject(Alice.Kind, 1);
        for (int i = 0; i < 5; i++)
        {
            store.Remember(mind, AttackedBy, strong, (ulong)i);
        }

        for (int i = 0; i < ICreatureMindStore.OpinionCapacity + 10; i++)
        {
            store.Remember(mind, FedBy, new MemorySubject(Alice.Kind, (ulong)(100 + i)), 10);
        }

        Assert.NotEqual(Opinion.Neutral, store.GetOpinionBaseline(mind, strong));
    }

    [Fact]
    public void Opinion_reads_do_not_allocate()
    {
        var (store, mind) = StoreWithMind(Calm);
        var opinions = new OpinionModel(store, ShippedRules());
        store.Remember(mind, FedBy, Alice, 0);
        store.Remember(mind, AttackedBy, Alice, 0);
        opinions.GetOpinion(mind, Alice, 1);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (ulong tick = 2; tick < 10_000; tick++)
        {
            opinions.GetOpinion(mind, Alice, tick);
            opinions.Knows(mind, Bob, tick);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
