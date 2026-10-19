using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Data.Definitions;
using static SumonerWorld.Tests.Core.Bonding.BondingTestKit;

namespace SumonerWorld.Tests.Core.Bonding;

public class BondingRulesTests
{
    [Fact]
    public void The_contract_version_is_1_0_0()
    {
        Assert.Equal("1.0.0", new BondingTestKit().Bonding.ContractVersion);
    }

    [Fact]
    public void A_refused_pact_is_remembered_and_pushing_again_costs_trust()
    {
        var kit = new BondingTestKit();
        PactTerms terms = PactTerms.Of(Term("FeedDaily"));

        BondingOutcome first = kit.Bonding.OfferPact(kit.Creature, Species(5000), Alice, terms, 0, 0, out PactDecision decision);
        int trustAfterOne = kit.Opinions.GetOpinion(kit.Creature, Alice, 1).Trust;
        for (int i = 1; i < 5; i++)
        {
            kit.Bonding.OfferPact(kit.Creature, Species(5000), Alice, terms, 0, (ulong)i, out _);
        }

        Assert.Equal(BondingOutcome.Refused, first);
        Assert.False(decision.Accepted);
        Assert.Equal(OwnershipState.Wild, kit.Bonding.GetBond(kit.Creature).State);
        Assert.Equal(5, kit.CountMemories("PactRefused", Alice, 5));
        Assert.True(kit.Opinions.GetOpinion(kit.Creature, Alice, 5).Trust < trustAfterOne, "A pushy player loses trust.");
    }

    [Fact]
    public void An_accepted_pact_binds_the_creature_writes_a_core_memory_and_raises_loyalty()
    {
        var kit = new BondingTestKit();
        int loyaltyBefore = kit.Store.GetPersonality(kit.Creature).Loyalty;

        Assert.Equal(BondingOutcome.Accepted, kit.Bond(Alice, now: 100));
        BondState bond = kit.Bonding.GetBond(kit.Creature);

        Assert.Equal(OwnershipState.Pacted, bond.State);
        Assert.Equal(Alice.Id, bond.Owner);
        Assert.Equal(1, kit.CountMemories("PactSealed", Alice, 100));
        Assert.Contains(kit.Store.GetCoreRecords(kit.Creature).ToArray(), record => record.EventType == Event("PactSealed"));
        Assert.Equal(loyaltyBefore + 2, kit.Store.GetPersonality(kit.Creature).Loyalty);
        Assert.Equal(BondingOutcome.WrongState, kit.Bonding.OfferPact(kit.Creature, Species(-1000), Bob, PactTerms.Of(0), 0, 101, out _));
    }

    [Fact]
    public void Only_the_owner_can_release_and_release_is_remembered()
    {
        var kit = new BondingTestKit();
        kit.Bond(Alice);

        Assert.Equal(BondingOutcome.NotOwner, kit.Bonding.Release(kit.Creature, Bob, 10));
        Assert.Equal(BondingOutcome.Ok, kit.Bonding.Release(kit.Creature, Alice, 10));
        Assert.Equal(OwnershipState.Wild, kit.Bonding.GetBond(kit.Creature).State);
        Assert.Equal(1, kit.CountMemories("ReleasedBy", Alice, 10));
    }

    [Fact]
    public void Kept_terms_build_trust_and_three_broken_terms_make_it_leave()
    {
        var kit = new BondingTestKit();
        kit.Bond(Alice);
        int feed = Term("FeedDaily");

        Assert.Equal(BondingOutcome.Ok, kit.Bonding.CheckTerms(kit.Creature, PactTerms.Of(feed), Day));
        Assert.Equal(1, kit.CountMemories("PactTermKept", Alice, Day));

        Assert.Equal(BondingOutcome.Ok, kit.Bonding.CheckTerms(kit.Creature, PactTerms.None, 2 * Day));
        Assert.Equal(BondingOutcome.Ok, kit.Bonding.CheckTerms(kit.Creature, PactTerms.None, 3 * Day));
        Assert.Equal(BondingOutcome.Left, kit.Bonding.CheckTerms(kit.Creature, PactTerms.None, 4 * Day));
        Assert.Equal(OwnershipState.Wild, kit.Bonding.GetBond(kit.Creature).State);
    }

    [Fact]
    public void An_aggressive_creature_that_leaves_turns_hostile()
    {
        var kit = new BondingTestKit(new Personality(0, 0, 0, Temper: 70));
        kit.Bond(Alice);

        kit.Bonding.CheckTerms(kit.Creature, PactTerms.None, Day);
        kit.Bonding.CheckTerms(kit.Creature, PactTerms.None, 2 * Day);

        Assert.Equal(BondingOutcome.LeftHostile, kit.Bonding.CheckTerms(kit.Creature, PactTerms.None, 3 * Day));
    }

    [Fact]
    public void Lasting_distrust_for_three_days_makes_it_leave_but_a_short_low_does_not()
    {
        var kit = new BondingTestKit();
        kit.Bond(Alice);
        for (int i = 0; i < 6; i++)
        {
            kit.Store.Remember(kit.Creature, Event("Mistreated"), Alice, (ulong)i);
            kit.Store.Remember(kit.Creature, Event("PactBroken"), Alice, (ulong)i);
        }

        Assert.True(kit.Opinions.GetOpinion(kit.Creature, Alice, 10).Trust < BondingRules.LeaveTrustBelow);
        Assert.Equal(BondingOutcome.Ok, kit.Bonding.DailyUpdate(kit.Creature, 10));
        Assert.Equal(BondingOutcome.Ok, kit.Bonding.DailyUpdate(kit.Creature, 10 + (2 * Day)));
        Assert.Equal(BondingOutcome.Left, kit.Bonding.DailyUpdate(kit.Creature, 10 + (3 * Day)));
    }

    [Fact]
    public void Care_over_days_raises_the_bond_level_and_neglect_lowers_it()
    {
        var kit = new BondingTestKit();
        kit.Bond(Alice);
        int start = kit.Bonding.RecalculateBondLevel(kit.Creature, 1);

        for (int day = 0; day < 20; day++)
        {
            ulong now = 2 + ((ulong)day * Day);
            kit.Store.Remember(kit.Creature, Event("FedBy"), Alice, now);
            kit.Store.Remember(kit.Creature, Event("HealedBy"), Alice, now);
            kit.Bonding.CheckTerms(kit.Creature, PactTerms.Of(Term("FeedDaily")), now);
        }

        int cared = kit.Bonding.RecalculateBondLevel(kit.Creature, 20 * Day);
        int neglected = kit.Bonding.RecalculateBondLevel(kit.Creature, 200 * Day);

        Assert.True(cared > start, $"Care should raise the bond level ({start} -> {cared}).");
        Assert.True(neglected < cared, $"Neglect should lower it again ({cared} -> {neglected}).");
        Assert.Equal(neglected, kit.Bonding.GetBond(kit.Creature).BondLevel);
    }

    [Fact]
    public void Bond_capacity_limits_summons_and_sanctum_residents_cannot_be_summoned()
    {
        var kit = new BondingTestKit();
        kit.Bond(Alice);

        Assert.Equal(BondingOutcome.CapacityReached, kit.Bonding.Summon(kit.Creature, Alice, activeSummons: 2, bondCapacity: 2));
        Assert.Equal(BondingOutcome.NotOwner, kit.Bonding.Summon(kit.Creature, Bob, 0, 2));
        Assert.Equal(BondingOutcome.Ok, kit.Bonding.Summon(kit.Creature, Alice, 1, 2));
        Assert.True(kit.Bonding.GetBond(kit.Creature).Summoned);
        Assert.Equal(BondingOutcome.Ok, kit.Bonding.Dismiss(kit.Creature, Alice));

        Assert.Equal(BondingOutcome.Ok, kit.Bonding.SetSanctum(kit.Creature, Alice, toSanctum: true));
        Assert.Equal(BondingOutcome.WrongState, kit.Bonding.Summon(kit.Creature, Alice, 0, 2));
        Assert.Equal(BondingOutcome.Ok, kit.Bonding.SetSanctum(kit.Creature, Alice, toSanctum: false));
        Assert.Throws<ArgumentOutOfRangeException>(() => kit.Bonding.Summon(kit.Creature, Alice, 0, BondingRules.MaxBondCapacity + 1));
    }

    [Fact]
    public void Death_is_permanent_on_hardcore_and_revivable_otherwise()
    {
        var hardcore = new BondingTestKit();
        var normal = new BondingTestKit();
        hardcore.Bond(Alice);
        normal.Bond(Alice);

        hardcore.Bonding.Die(hardcore.Creature, permanent: true);
        normal.Bonding.Die(normal.Creature, permanent: false);

        Assert.Equal(BondingOutcome.WrongState, hardcore.Bonding.Revive(hardcore.Creature, Alice));
        Assert.Equal(BondingOutcome.NotOwner, normal.Bonding.Revive(normal.Creature, Bob));
        Assert.Equal(BondingOutcome.Ok, normal.Bonding.Revive(normal.Creature, Alice));
        Assert.Equal(OwnershipState.Pacted, normal.Bonding.GetBond(normal.Creature).State);
    }

    [Fact]
    public void Re_pacting_needs_the_creatures_consent_and_its_memories_travel_with_it()
    {
        var kit = new BondingTestKit();
        kit.Bond(Alice);
        kit.Store.Remember(kit.Creature, Event("AttackedBy"), Bob, 1);
        kit.Store.Remember(kit.Creature, Event("KilledKin"), Bob, 1);

        BondingOutcome refused = kit.Bonding.Repact(kit.Creature, Species(400), Alice, Bob, PactTerms.Of(0), 0, 2, out PactDecision decision);
        Assert.Equal(BondingOutcome.Refused, refused);
        Assert.True(decision.GrudgePenalty > 0);
        Assert.Equal(Alice.Id, kit.Bonding.GetBond(kit.Creature).Owner);

        Assert.Equal(BondingOutcome.NotOwner, kit.Bonding.Repact(kit.Creature, Species(-1000), Bob, Bob, PactTerms.Of(0), 0, 3, out _));
        Assert.Equal(BondingOutcome.Accepted, kit.Bonding.Repact(kit.Creature, Species(-5000), Alice, Bob, PactTerms.Of(0), 0, 3, out _));
        Assert.Equal(Bob.Id, kit.Bonding.GetBond(kit.Creature).Owner);
        Assert.True(kit.CountMemories("KilledKin", Bob, 3) > 0, "Memories travel with the creature.");
    }

    [Fact]
    public void A_new_creature_in_a_reused_slot_starts_wild()
    {
        var kit = new BondingTestKit();
        kit.Bond(Alice);
        kit.Store.Release(kit.Creature);

        MindHandle reused = kit.Store.Allocate(new Personality(0, 0, 0, 0), SumonerWorld.Tests.Core.Cognition.CognitionTestData.Needs);

        Assert.Equal(kit.Creature.Slot, reused.Slot);
        Assert.Equal(OwnershipState.Wild, kit.Bonding.GetBond(reused).State);
    }

    [Fact]
    public void Pacts_are_made_with_players_only()
    {
        var kit = new BondingTestKit();

        Assert.Throws<ArgumentException>(() =>
            kit.Bonding.OfferPact(kit.Creature, Species(0), new MemorySubject(SubjectKind.Creature, 5), PactTerms.Of(0), 0, 0, out _));
    }
}
