using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Cognition.Memory;
using static SumonerWorld.Tests.Core.Bonding.BondingTestKit;

namespace SumonerWorld.Tests.Core.Bonding;

public class PactEvaluatorTests
{
    [Fact]
    public void A_hungry_creature_values_food_more_and_an_independent_one_values_freedom_more()
    {
        var kit = new BondingTestKit(new Personality(0, Loyalty: -80, 0, 0));
        var loyal = new BondingTestKit(new Personality(0, Loyalty: 80, 0, 0));
        int fed = kit.Evaluator.TermValue(kit.Creature, Term("FeedDaily"), 0);
        kit.Store.SetNeed(kit.Creature, Need.Hunger, 100, 0);
        int hungry = kit.Evaluator.TermValue(kit.Creature, Term("FeedDaily"), 0);

        Assert.True(hungry > fed);
        Assert.True(kit.Evaluator.TermValue(kit.Creature, Term("FreedomAtNight"), 0)
            > loyal.Evaluator.TermValue(loyal.Creature, Term("FreedomAtNight"), 0));
    }

    [Fact]
    public void Trust_raises_the_score_and_fear_and_grudges_lower_it_with_every_part_reported()
    {
        var neutral = new BondingTestKit();
        var trusted = new BondingTestKit();
        var feared = new BondingTestKit();
        var hated = new BondingTestKit();
        trusted.Store.Remember(trusted.Creature, Event("HealedBy"), Alice, 0);
        feared.Store.Remember(feared.Creature, Event("AttackedBy"), Alice, 0);
        hated.Store.Remember(hated.Creature, Event("KilledKin"), Alice, 0);
        PactTerms terms = PactTerms.Of(Term("FeedDaily"), Term("ProtectYoung"));

        PactDecision baseline = neutral.Evaluator.Evaluate(neutral.Creature, Species(500), Alice, terms, 0, 1);
        PactDecision withTrust = trusted.Evaluator.Evaluate(trusted.Creature, Species(500), Alice, terms, 0, 1);
        PactDecision withFear = feared.Evaluator.Evaluate(feared.Creature, Species(500), Alice, terms, 0, 1);
        PactDecision withGrudge = hated.Evaluator.Evaluate(hated.Creature, Species(500), Alice, terms, 0, 1);

        Assert.True(withTrust.Score > baseline.Score && withTrust.Trust > 0);
        Assert.True(withFear.Score < baseline.Score && withFear.FearPenalty > 0);
        Assert.Equal(PactEvaluator.GrudgePenalty, withGrudge.GrudgePenalty);
        Assert.Equal(
            baseline.Terms + baseline.Trust + baseline.Respect + baseline.Reputation - baseline.FearPenalty - baseline.GrudgePenalty,
            baseline.Score);
    }

    [Fact]
    public void Reputation_counts_and_acceptance_is_exactly_score_against_threshold_with_no_randomness()
    {
        var kit = new BondingTestKit();
        PactTerms terms = PactTerms.Of(Term("FeedDaily"));
        PactDecision plain = kit.Evaluator.Evaluate(kit.Creature, Species(0), Alice, terms, 0, 0);
        PactDecision famous = kit.Evaluator.Evaluate(kit.Creature, Species(0), Alice, terms, 1000, 0);

        Assert.Equal(plain.Score + 200, famous.Score);
        Assert.True(kit.Evaluator.Evaluate(kit.Creature, Species(plain.Score), Alice, terms, 0, 0).Accepted);
        Assert.False(kit.Evaluator.Evaluate(kit.Creature, Species(plain.Score + 1), Alice, terms, 0, 0).Accepted);
        Assert.Equal(plain, kit.Evaluator.Evaluate(kit.Creature, Species(0), Alice, terms, 0, 0));
    }

    [Theory]
    [InlineData(0b0000_0000)]
    [InlineData(0b0000_1111)]
    [InlineData(0b1000_0000)]
    public void Offers_need_one_to_three_terms_from_the_catalogue(byte mask)
    {
        var kit = new BondingTestKit();

        Assert.Throws<ArgumentException>(() => kit.Evaluator.Evaluate(kit.Creature, Species(0), Alice, new PactTerms(mask), 0, 0));
    }
}
