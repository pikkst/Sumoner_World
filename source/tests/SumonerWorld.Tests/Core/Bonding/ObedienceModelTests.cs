using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Cognition;
using static SumonerWorld.Tests.Core.Bonding.BondingTestKit;

namespace SumonerWorld.Tests.Core.Bonding;

public class ObedienceModelTests
{
    [Fact]
    public void A_higher_bond_level_makes_a_creature_more_obedient()
    {
        var kit = new BondingTestKit();

        ObedienceResult fresh = kit.Obedience.Evaluate(kit.Creature, Alice, bondLevel: 0, danger: 0, false, false, 0);
        ObedienceResult devoted = kit.Obedience.Evaluate(kit.Creature, Alice, bondLevel: 4, danger: 0, false, false, 0);

        Assert.True(devoted.Obedience > fresh.Obedience);
        Assert.Equal(CommandResponse.Obey, devoted.Response);
    }

    [Fact]
    public void The_four_responses_follow_the_score_thresholds()
    {
        var kit = new BondingTestKit(new Personality(0, Loyalty: 60, 0, 0));
        kit.Bond(Alice);

        Assert.Equal(CommandResponse.Obey, Response(kit, danger: 0));
        Assert.Equal(CommandResponse.Improvise, Response(kit, danger: 400));
        Assert.Equal(CommandResponse.Hesitate, Response(kit, danger: 700));
        Assert.Equal(CommandResponse.Refuse, Response(kit, danger: 700, conflict: true));
    }

    [Fact]
    public void A_suicidal_command_is_refused_by_a_creature_with_low_loyalty_only()
    {
        var independent = new BondingTestKit(new Personality(0, Loyalty: 20, 0, 0));
        var loyal = new BondingTestKit(new Personality(0, Loyalty: 90, 0, 0));

        ObedienceResult refused = independent.Obedience.Evaluate(independent.Creature, Alice, 5, danger: 900, false, false, 0);
        ObedienceResult loyalAnswer = loyal.Obedience.Evaluate(loyal.Creature, Alice, 5, danger: 900, false, false, 0);

        Assert.Equal(CommandResponse.Refuse, refused.Response);
        Assert.True(refused.Suicidal);
        Assert.False(loyalAnswer.Suicidal);
        Assert.NotEqual(CommandResponse.Refuse, loyalAnswer.Response);
    }

    [Fact]
    public void Knowing_a_better_tactic_turns_obedience_into_improvisation()
    {
        var kit = new BondingTestKit();

        Assert.Equal(CommandResponse.Improvise, kit.Obedience.Evaluate(kit.Creature, Alice, 5, 0, false, knowsBetterTactic: true, 0).Response);
    }

    [Fact]
    public void Fear_of_the_owner_lowers_obedience()
    {
        var kit = new BondingTestKit();
        int before = kit.Obedience.Evaluate(kit.Creature, Alice, 2, 0, false, false, 0).Obedience;
        kit.Store.Remember(kit.Creature, Event("AttackedBy"), Alice, 1);

        Assert.True(kit.Obedience.Evaluate(kit.Creature, Alice, 2, 0, false, false, 1).Obedience < before);
    }

    private static CommandResponse Response(BondingTestKit kit, int danger, bool conflict = false) =>
        kit.Obedience.Evaluate(kit.Creature, Alice, bondLevel: 1, danger, conflict, knowsBetterTactic: false, 0).Response;
}
