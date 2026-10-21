using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Intents;
using SumonerWorld.Net.Messages;

namespace SumonerWorld.Net.Client;

/// <summary>
/// What the player reads after an interaction (M1-23 Memory Grove). It reports only what the player did and saw happen, never what
/// the creature thinks or remembers: the playtest measures whether body language alone carries memory (QA plan B2, DEC-002).
/// </summary>
public static class InteractionText
{
    /// <summary>The key hints shown when a creature is in reach.</summary>
    public const string Hints = "[E] feed   [F] hit   [R] free from snare   [P] offer a Pact";

    /// <summary>The hint for the shout, shown all the time.</summary>
    public const string ShoutHint = "[Q] shout";

    /// <summary>The line shown for an intent's result; <paramref name="creature"/> is the target's display name (unused for Scare).</summary>
    public static string Result(IntentKind kind, IntentResult result, string creature)
    {
        if (!result.IsApplied)
        {
            return result.Reason switch
            {
                IntentRejectReason.OutOfRange => $"The {creature} is too far away.",
                IntentRejectReason.OnCooldown => "Not so fast.",
                IntentRejectReason.WrongState when kind == IntentKind.Help => $"The {creature} is not caught in anything.",
                IntentRejectReason.WrongState => $"The {creature} is already bonded.",
                IntentRejectReason.InvalidTarget => "It is gone.",
                _ => "Nothing happens.",
            };
        }

        return kind switch
        {
            IntentKind.Feed => $"You feed the {creature}.",
            IntentKind.Hit => $"You strike at the {creature}.",
            IntentKind.Help => $"You free the {creature} from the snare.",
            IntentKind.Scare when result.Detail == 0 => "You shout. Nothing answers.",
            IntentKind.Scare => "You shout.",
            IntentKind.OfferPact => (BondingOutcome)result.Detail == BondingOutcome.Accepted
                ? $"The {creature} accepts your Pact."
                : $"The {creature} turns away from your Pact.",
            _ => string.Empty,
        };
    }

    /// <summary>A species id as a display name (until localization: <c>hollow_owl</c> → <c>hollow owl</c>).</summary>
    public static string DisplayName(string speciesId)
    {
        ArgumentNullException.ThrowIfNull(speciesId);
        return speciesId.Replace('_', ' ');
    }
}
