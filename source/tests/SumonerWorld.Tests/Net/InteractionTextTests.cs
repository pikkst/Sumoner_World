using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Intents;
using SumonerWorld.Net.Client;
using SumonerWorld.Net.Messages;

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
        ];
        string[] forbidden = ["remember", "memory", "trust", "fear", "opinion", "likes", "hates", "angry", "scared"];
        Assert.DoesNotContain(lines, line => forbidden.Any(word => line.Contains(word, StringComparison.OrdinalIgnoreCase)));
        Assert.Equal("hollow owl", InteractionText.DisplayName("hollow_owl"));
    }
}
