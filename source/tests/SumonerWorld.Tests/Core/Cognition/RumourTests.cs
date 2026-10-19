using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Cognition.Opinions;
using SumonerWorld.Core.Data.Definitions;
using static SumonerWorld.Tests.Core.Cognition.CognitionTestData;

namespace SumonerWorld.Tests.Core.Cognition;

public class RumourTests
{
    private static readonly Personality Bold = new(60, 0, 0, 0);

    [Fact]
    public void A_groupmate_hears_the_strongest_memories_about_players_at_half_strength()
    {
        var (store, witness, listener) = Pair();
        store.Remember(witness, KilledKin, Alice, 0);
        store.Remember(witness, AttackedBy, Bob, 0);
        store.Remember(witness, FedBy, Alice, 0);
        store.Remember(witness, HealedBy, Bob, 0);
        store.Remember(witness, BondedDied, new MemorySubject(SubjectKind.Creature, 9), 0);

        int shared = store.ShareRumours(witness, listener, 10, maxShared: 3);

        MemoryRecord[] heard = store.GetEpisodicRecords(listener).ToArray();
        Assert.Equal(3, shared);
        Assert.All(heard, record => Assert.Equal(MemoryTags.Rumour, record.Tags));
        Assert.Equal([KilledKin, AttackedBy, HealedBy], heard.Select(record => record.EventType));
        Assert.Equal(450, heard[0].Salience);
    }

    [Fact]
    public void Rumours_are_never_passed_on_and_never_override_first_hand_experience()
    {
        var (store, witness, listener) = Pair();
        MindHandle third = store.Allocate(Bold, Needs);
        store.Remember(witness, KilledKin, Alice, 0);
        store.Remember(listener, FedBy, Alice, 0);

        store.ShareRumours(witness, listener, 1, 3);
        int passedOn = store.ShareRumours(listener, third, 2, 3);
        int overwritten = store.ShareRumours(third, listener, 2, 3);

        Assert.Equal(1, passedOn);
        Assert.Equal(FedBy, store.GetEpisodicRecords(third).ToArray().Single().EventType);
        Assert.Equal(0, overwritten);
    }

    [Fact]
    public void A_rumour_shapes_opinion_but_creates_no_grudge_and_no_baseline()
    {
        var (store, witness, listener) = Pair();
        MemoryRules rules = ShippedRules();
        var opinions = new OpinionModel(store, rules);
        var appraisal = new AppraisalModel(store, opinions, rules, AppraisalSettings.Default);
        store.Remember(witness, KilledKin, Alice, 0);

        store.ShareRumours(witness, listener, 1, 3);

        Assert.True(opinions.GetOpinion(listener, Alice, 1).Trust < 0, "The herd distrusts the kin-killer it heard about.");
        Assert.Equal(Opinion.Neutral, store.GetOpinionBaseline(listener, Alice));
        Assert.NotEqual(Emote.Grudge, appraisal.Appraise(listener, Alice, 1));
        Assert.Equal(Emote.Grudge, appraisal.Appraise(witness, Alice, 1));
    }

    [Fact]
    public void Experiencing_it_first_hand_replaces_the_rumour_so_it_can_be_shared_again()
    {
        var (store, witness, listener) = Pair();
        MindHandle third = store.Allocate(Bold, Needs);
        store.Remember(witness, AttackedBy, Alice, 0);
        store.ShareRumours(witness, listener, 1, 3);

        store.Remember(listener, AttackedBy, Alice, 2);

        MemoryRecord record = store.GetEpisodicRecords(listener).ToArray().Single();
        Assert.Equal(MemoryTags.None, record.Tags);
        Assert.Equal(1, store.ShareRumours(listener, third, 3, 3));
    }

    [Fact]
    public void Sharing_does_not_allocate()
    {
        var (store, witness, listener) = Pair();
        store.Remember(witness, KilledKin, Alice, 0);
        store.Remember(witness, AttackedBy, Bob, 0);
        store.ShareRumours(witness, listener, 1, 3);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (ulong tick = 2; tick < 3_000; tick++)
        {
            store.ShareRumours(witness, listener, tick, 3);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static (CreatureMindStore Store, MindHandle Witness, MindHandle Listener) Pair()
    {
        var store = new CreatureMindStore(ShippedRules(), capacity: 4);
        return (store, store.Allocate(Bold, Needs), store.Allocate(Bold, Needs));
    }
}
