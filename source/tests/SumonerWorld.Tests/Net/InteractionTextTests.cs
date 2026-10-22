using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Items;
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
        Assert.Equal("You pick up the loot (+7 gold).", InteractionText.Result(IntentKind.PickUp, IntentResult.Applied(7), "loot"));
        Assert.Equal("You gather 3 sweet berries.", InteractionText.Result(IntentKind.Gather, IntentResult.Applied(3), "sweet berries"));
        Assert.Equal(
            "Your bag is full.",
            InteractionText.Result(IntentKind.Gather, new IntentResult(IntentStatus.Rejected, IntentRejectReason.WrongState, (byte)SumonerWorld.Core.Items.ItemRefusal.InventoryFull), "x"));
        Assert.Equal("[H] pick berries", InteractionText.GatherHint("berry_bush"));
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

    [Fact]
    public void Craft_and_gear_results_say_what_happened_or_why_not()
    {
        Assert.StartsWith("You start making: Wooden staff.", InteractionText.Result(IntentKind.Craft, IntentResult.Applied(), "Wooden staff"), StringComparison.Ordinal);
        Assert.Equal("You put on: Leather vest.", InteractionText.Result(IntentKind.Equip, IntentResult.Applied(3), "Leather vest"));
        Assert.Equal("You take off: Leather vest.", InteractionText.Result(IntentKind.Unequip, IntentResult.Applied(), "Leather vest"));
        var missing = new IntentResult(IntentStatus.Rejected, IntentRejectReason.WrongState, (byte)GearRefusal.MissingInputs);
        Assert.Equal("You do not have everything for Wooden staff.", InteractionText.Result(IntentKind.Craft, missing, "Wooden staff"));
        var level = new IntentResult(IntentStatus.Rejected, IntentRejectReason.WrongState, (byte)GearRefusal.LevelTooLow);
        Assert.Equal("Not strong enough for Leather vest yet.", InteractionText.Result(IntentKind.Equip, level, "Leather vest"));
        Assert.Equal("Call that companion to your side first.", InteractionText.Result(IntentKind.Equip, IntentResult.Reject(IntentRejectReason.InvalidTarget), "x"));
        Assert.Equal("charm", InteractionText.PositionName(EquipPosition.Charm2));
    }

    [Fact]
    public void Profiles_show_class_rank_level_cap_experience_and_bond_capacity()
    {
        var growth = new SumonerWorld.Core.Progression.GrowthRules(ShippedData.Current.Combat.Growth);
        var profile = new RosterEntry(1002, 0, 2, 3, 150);

        Assert.Equal("Summoner 1002\nElf · Rank D · Level 3", InteractionText.Nameplate(profile));
        string lines = InteractionText.ProfileLines(profile, growth, ShippedData.Current.Combat);
        Assert.Contains("(cap 30)", lines, StringComparison.Ordinal);
        Assert.Contains($"/ {growth.XpFor(4)} for level 4", lines, StringComparison.Ordinal);
        Assert.Contains("Bond Capacity 2", lines, StringComparison.Ordinal);
        Assert.Contains("rank up", InteractionText.ProfileLines(profile with { Rank = 0, Level = 10 }, growth, ShippedData.Current.Combat), StringComparison.Ordinal);
    }
}
