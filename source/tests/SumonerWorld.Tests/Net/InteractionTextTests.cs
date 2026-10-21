using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Intents;
using SumonerWorld.Net.Client;
using SumonerWorld.Net.Messages;
using SumonerWorld.Scenarios;

namespace SumonerWorld.Tests.Net;

public class InteractionTextTests
{
    private static readonly int[] Details = [0, 1, 2];

    [Theory]
    [InlineData(IntentKind.Feed, 0, "You feed the mossback.")]
    [InlineData(IntentKind.Hit, 0, "You strike at the mossback.")]
    [InlineData(IntentKind.Help, 0, "You free the mossback from the snare.")]
    [InlineData(IntentKind.Scare, 0, "You shout. Nothing answers.")]
    [InlineData(IntentKind.Scare, 2, "You shout.")]
    [InlineData(IntentKind.OfferPact, (int)BondingOutcome.Accepted, "The mossback accepts your Pact.")]
    [InlineData(IntentKind.OfferPact, (int)BondingOutcome.Refused, "The mossback turns away from your Pact.")]
    public void Applied_interactions_say_what_the_player_did_and_saw(IntentKind kind, int detail, string expected) =>
        Assert.Equal(expected, InteractionText.Result(kind, IntentResult.Applied((byte)detail), "mossback"));

    [Theory]
    [InlineData(IntentKind.Feed, IntentRejectReason.OutOfRange, "The bogfox is too far away.")]
    [InlineData(IntentKind.Hit, IntentRejectReason.OnCooldown, "Not so fast.")]
    [InlineData(IntentKind.Help, IntentRejectReason.WrongState, "The bogfox is not caught in anything.")]
    [InlineData(IntentKind.OfferPact, IntentRejectReason.WrongState, "The bogfox is already bonded.")]
    [InlineData(IntentKind.Feed, IntentRejectReason.InvalidTarget, "It is gone.")]
    public void Refusals_explain_themselves(IntentKind kind, IntentRejectReason reason, string expected) =>
        Assert.Equal(expected, InteractionText.Result(kind, IntentResult.Reject(reason), "bogfox"));

    [Fact]
    public void No_line_mentions_what_a_creature_thinks_or_remembers()
    {
        string[] lines =
        [
            .. Enum.GetValues<IntentKind>().SelectMany(kind => Details.Select(detail => InteractionText.Result(kind, IntentResult.Applied((byte)detail), "x"))),
            InteractionText.Hints,
            InteractionText.ShoutHint,
            InteractionText.PactMenuHints,
            InteractionText.CompanionHints,
            InteractionText.CompanionLine("x", 1, SumonerWorld.Core.Progression.LifeStage.Young, true),
            InteractionText.CompanionLine("x", 9, SumonerWorld.Core.Progression.LifeStage.Elder, false),
            .. Enum.GetValues<SummonRefusal>().Select(refusal => InteractionText.Result(IntentKind.Summon, new IntentResult(IntentStatus.Rejected, IntentRejectReason.WrongState, (byte)refusal), "x")),
            InteractionText.PactMenuTitle("x"),
            .. ShippedData.Current.PactTerms.Select(term => InteractionText.PactTerm(term.Key)),
        ];
        string[] forbidden = ["remember", "memory", "trust", "fear", "opinion", "likes", "hates", "angry", "scared"];
        Assert.DoesNotContain(lines, line => forbidden.Any(word => line.Contains(word, StringComparison.OrdinalIgnoreCase)));
        Assert.Equal("hollow owl", InteractionText.DisplayName("hollow_owl"));
    }

    [Fact]
    public void A_companion_line_shows_level_stage_and_where_it_is()
    {
        Assert.Equal("hollow owl Lv 3 (young): with you", InteractionText.CompanionLine("hollow owl", 3, SumonerWorld.Core.Progression.LifeStage.Young, summoned: true));
        Assert.Equal(
            "The mossback does not answer to your kind of summoner.",
            InteractionText.Result(IntentKind.OfferPact, IntentResult.Applied((byte)BondingOutcome.WrongClass), "mossback"));
        Assert.Equal("Elf · Rank D · Level 23", InteractionText.PlayerLine(SumonerWorld.Core.Progression.SummonerClass.Elf, SumonerWorld.Core.Progression.Rank.D, 23));
        Assert.Equal("bogfox Lv 8 (elder): away", InteractionText.CompanionLine("bogfox", 8, SumonerWorld.Core.Progression.LifeStage.Elder, summoned: false));
    }

    [Fact]
    public void Skills_read_as_names_effects_and_states()
    {
        var lunge = new SumonerWorld.Core.Data.Definitions.SkillNodeDefinition { Id = "lunge", Branch = "pack", Move = "lunge" };
        var hide = new SumonerWorld.Core.Data.Definitions.SkillNodeDefinition
        {
            Id = "thick_hide",
            Branch = "guardian",
            Passive = new() { Stat = SumonerWorld.Core.Data.Definitions.SkillStat.Health, Amount = 15 },
        };

        Assert.Equal("Thick hide", InteractionText.SkillName("thick_hide"));
        Assert.Equal("Your mossback learned your style: Uproot! It will join in when you cast it.", InteractionText.ComboLearned("mossback", "uproot"));
        Assert.Equal("new move: Lunge", InteractionText.SkillEffect(lunge));
        Assert.Equal("health +15%", InteractionText.SkillEffect(hide));
        Assert.Equal("likes it", InteractionText.BranchFeeling(40));
        Assert.Equal("dislikes it: skills cost 2", InteractionText.BranchFeeling(-40));
        Assert.Equal(
            "Not yet: needs a stronger bond.",
            InteractionText.Result(IntentKind.LearnSkill, new IntentResult(IntentStatus.Rejected, IntentRejectReason.WrongState, (byte)SumonerWorld.Core.Progression.SkillState.NeedsBond), "bogfox"));
    }

    [Theory]
    [InlineData(SummonRefusal.NoMana, "Not enough mana to draw the sigil.")]
    [InlineData(SummonRefusal.CapacityReached, "You cannot call more companions now. Send one back first.")]
    [InlineData(SummonRefusal.AlreadySummoned, "The bogfox is already with you.")]
    public void A_refused_summon_says_why(SummonRefusal refusal, string expected)
    {
        var result = new IntentResult(IntentStatus.Rejected, IntentRejectReason.WrongState, (byte)refusal);
        Assert.Equal(expected, InteractionText.Result(IntentKind.Summon, result, "bogfox"));
        Assert.Equal("You draw a sigil and call the bogfox.", InteractionText.Result(IntentKind.Summon, IntentResult.Applied(), "bogfox"));
        Assert.Equal("You send the bogfox back.", InteractionText.Result(IntentKind.Dismiss, IntentResult.Applied(), "bogfox"));
    }

    [Fact]
    public void Every_shipped_Pact_term_has_a_promise_line_and_none_shows_a_chance()
    {
        foreach (var term in ShippedData.Current.PactTerms)
        {
            string line = InteractionText.PactTerm(term.Key);
            Assert.NotEqual(term.Key, line);
            Assert.DoesNotContain("%", line, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("chance", InteractionText.PactMenuTitle("mossback") + InteractionText.PactMenuHints, StringComparison.OrdinalIgnoreCase);
    }
}
