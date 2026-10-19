using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Cognition.Opinions;
using SumonerWorld.Core.Data.Definitions;
using static SumonerWorld.Tests.Core.Cognition.CognitionTestData;

namespace SumonerWorld.Tests.Core.Cognition;

[Collection(AllocationMeasurements.Name)]
public class AppraisalModelTests
{
    private static readonly Personality Bold = new(Boldness: 60, Loyalty: 0, Curiosity: 0, Temper: 20);
    private static readonly Personality Timid = new(Boldness: -60, Loyalty: 0, Curiosity: 0, Temper: -20);
    private static readonly Personality Curious = new(Boldness: 0, Loyalty: 0, Curiosity: 80, Temper: 0);

    [Fact]
    public void A_stranger_makes_a_curious_creature_curious_and_a_wary_one_indifferent()
    {
        var (store, appraisal, curious) = Create(Curious);
        MindHandle wary = store.Allocate(new Personality(0, 0, -80, 0), Needs);

        Assert.Equal(Emote.Curiosity, appraisal.Appraise(curious, Alice, 0));
        Assert.Equal(Emote.None, appraisal.Appraise(wary, Alice, 0));
    }

    [Fact]
    public void Someone_who_killed_its_kin_gets_a_grudge_from_a_bold_creature_and_fear_from_a_timid_one()
    {
        var (store, appraisal, bold) = Create(Bold);
        MindHandle timid = store.Allocate(Timid, Needs);
        store.Remember(bold, KilledKin, Alice, 0);
        store.Remember(timid, KilledKin, Alice, 0);

        Assert.Equal(Emote.Grudge, appraisal.Appraise(bold, Alice, 10));
        Assert.Equal(Emote.Fear, appraisal.Appraise(timid, Alice, 10));
        Assert.True(appraisal.GetEmotions(bold, 10).Anger > 0, "Hostility makes bold creatures angry (AI Bible §6.1).");
        Assert.NotEqual(Emote.Grudge, appraisal.Appraise(bold, Bob, 10));
    }

    [Fact]
    public void Being_attacked_makes_it_afraid_of_the_attacker()
    {
        var (store, appraisal, mind) = Create(Timid);
        store.Remember(mind, AttackedBy, Alice, 0);

        Assert.Equal(Emote.Fear, appraisal.Appraise(mind, Alice, 1));
    }

    [Fact]
    public void Kindness_over_time_shows_as_trust_then_affection()
    {
        var (store, appraisal, mind) = Create(Bold);
        store.Remember(mind, SavedLifeOf, Alice, 0);

        Emote afterRescue = appraisal.Appraise(mind, Alice, 1);
        for (int day = 0; day < 15; day++)
        {
            store.Remember(mind, HealedBy, Alice, 2 + ((ulong)day * TicksPerDay));
            store.Remember(mind, FedBy, Alice, 3 + ((ulong)day * TicksPerDay));
        }

        Emote afterCare = appraisal.Appraise(mind, Alice, 4 + (14UL * TicksPerDay));

        Assert.True(afterRescue is Emote.Trust or Emote.Affection, $"A rescued creature should trust its rescuer, got {afterRescue}.");
        Assert.Equal(Emote.Affection, afterCare);
    }

    [Fact]
    public void A_recent_death_shows_grief_until_the_grief_window_passes()
    {
        var (store, appraisal, mind) = Create(Bold);
        store.Remember(mind, BondedDied, new MemorySubject(SubjectKind.Creature, 7), 0);

        Assert.Equal(Emote.Grief, appraisal.Appraise(mind, Alice, 100));
        Assert.NotEqual(Emote.Grief, appraisal.Appraise(mind, Alice, (ulong)AppraisalSettings.Default.GriefWindowTicks + 101));
    }

    [Fact]
    public void Emotions_fall_back_to_the_personality_baseline()
    {
        var (_, appraisal, mind) = Create(Timid);
        int baselineFear = appraisal.GetEmotions(mind, 0).Fear;

        appraisal.Stimulate(mind, Emotion.Fear, 80, 0);
        int peak = appraisal.GetEmotions(mind, 0).Fear;
        int halfLife = appraisal.GetEmotions(mind, (ulong)AppraisalSettings.Default.EmotionHalfLifeTicks).Fear;
        int muchLater = appraisal.GetEmotions(mind, 100UL * (ulong)AppraisalSettings.Default.EmotionHalfLifeTicks).Fear;

        Assert.Equal(15, baselineFear);
        Assert.Equal(95, peak);
        Assert.InRange(halfLife, 54, 56);
        Assert.Equal(baselineFear, muchLater);
    }

    [Fact]
    public void A_new_creature_in_a_reused_slot_starts_with_fresh_emotions()
    {
        var (store, appraisal, mind) = Create(Timid);
        appraisal.Stimulate(mind, Emotion.Anger, 90, 0);
        store.Release(mind);

        MindHandle reused = store.Allocate(Bold, Needs);

        Assert.Equal(mind.Slot, reused.Slot);
        Assert.Equal(Math.Max(0, (int)Bold.Temper) / 4, appraisal.GetEmotions(reused, 1).Anger);
    }

    [Fact]
    public void The_dominant_emotion_breaks_ties_in_a_fixed_order()
    {
        Assert.Equal(Emotion.Fear, new EmotionState(10, 10, 10, 10, 10).Dominant);
        Assert.Equal(Emotion.Joy, new EmotionState(10, 20, 30, 30, 0).Dominant);
    }

    [Fact]
    public void Appraising_does_not_allocate()
    {
        var (store, appraisal, mind) = Create(Bold);
        store.Remember(mind, KilledKin, Alice, 0);
        store.Remember(mind, FedBy, Bob, 0);
        appraisal.Appraise(mind, Alice, 1);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (ulong tick = 2; tick < 5_000; tick++)
        {
            appraisal.Appraise(mind, Alice, tick);
            appraisal.Appraise(mind, Bob, tick);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static (CreatureMindStore Store, AppraisalModel Appraisal, MindHandle Mind) Create(Personality personality)
    {
        var (store, mind) = StoreWithMind(personality);
        MemoryRules rules = ShippedRules();
        var appraisal = new AppraisalModel(store, new OpinionModel(store, rules), rules, AppraisalSettings.Default);
        return (store, appraisal, mind);
    }
}
