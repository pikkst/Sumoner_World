using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Progression;
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

    /// <summary>
    /// Seconds between a Pact answer arriving and its line appearing: the creature shows its answer with its body first (it stops
    /// to consider for half a second, then comes closer or turns away), the text follows (M2-05).
    /// </summary>
    public const double PactAnswerDelaySeconds = 1.5;

    /// <summary>The player's level line in the HUD, e.g. <c>Elf · Rank F · Level 3</c> (M2-09).</summary>
    public static string PlayerLine(SummonerClass summonerClass, Rank rank, int level) => $"{summonerClass} · Rank {rank} · Level {level}";

    /// <summary>The companion keys, shown under the companion list.</summary>
    public const string CompanionHints = "[Z] call   [X] send back\n[1] attack   [2] guard me   [3] hold here   [4] come back";

    /// <summary>The line after a command was given (the companions answer with their bodies).</summary>
    public static string CommandSent(CompanionCommand command) => command switch
    {
        CompanionCommand.Attack => "You call: attack!",
        CompanionCommand.GuardMe => "You call: guard me!",
        CompanionCommand.Hold => "You call: hold here!",
        CompanionCommand.Retreat => "You call: come back!",
        _ => string.Empty,
    };

    /// <summary>A companion's line in the list: its name, level and life stage, and whether it is at the player's side.</summary>
    public static string CompanionLine(string creature, int level, LifeStage stage, bool summoned) =>
        $"{creature} Lv {level} ({stage.ToString().ToLowerInvariant()}): {(summoned ? "with you" : "away")}";

    /// <summary>The keys shown at the bottom of the Pact menu.</summary>
    public const string PactMenuHints = "[1-6] choose up to 3 promises   [Enter] offer   [Esc] cancel";

    /// <summary>The Pact menu's title for a creature.</summary>
    public static string PactMenuTitle(string creature) => $"Offer a Pact to the {creature}. What do you promise?";

    /// <summary>A Pact Term as the player promises it (until localization, R39); unknown keys are shown as they are.</summary>
    public static string PactTerm(string key) => key switch
    {
        "FeedDaily" => "I will feed you every day.",
        "TerritoryNearSanctum" => "You may make your home near my Sanctum.",
        "ShareOfLoot" => "You get a share of what we find.",
        "FreedomAtNight" => "You are free to roam at night.",
        "NoFightingKin" => "I will never ask you to fight your own kind.",
        "ProtectYoung" => "I will protect your young.",
        _ => key,
    };

    /// <summary>The line shown for an intent's result; <paramref name="creature"/> is the target's display name (unused for Scare).</summary>
    public static string Result(IntentKind kind, IntentResult result, string creature, byte argument = 0)
    {
        if (!result.IsApplied && kind == IntentKind.Summon)
        {
            return (SummonRefusal)result.Detail switch
            {
                SummonRefusal.NoMana => "Not enough mana to draw the sigil.",
                SummonRefusal.CapacityReached => "You cannot call more companions now. Send one back first.",
                SummonRefusal.Busy => "Not now.",
                SummonRefusal.AlreadySummoned => $"The {creature} is already with you.",
                SummonRefusal.NotPacted => $"The {creature} is not bonded to you.",
                _ => "Nothing happens.",
            };
        }

        if (kind == IntentKind.Command)
        {
            return result.IsApplied ? CommandSent((CompanionCommand)argument) : result.Reason switch
            {
                IntentRejectReason.WrongState => "No companion is with you. Call one first [Z].",
                IntentRejectReason.InvalidTarget => "Choose a creature to attack: lock on with T, or stand near it.",
                IntentRejectReason.OutOfRange => "That is too far away.",
                _ => "Nothing happens.",
            };
        }

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
            IntentKind.Summon => $"You draw a sigil and call the {creature}.",
            IntentKind.Dismiss => $"You send the {creature} back.",
            IntentKind.OfferPact => (BondingOutcome)result.Detail switch
            {
                BondingOutcome.Accepted => $"The {creature} accepts your Pact.",
                BondingOutcome.WrongClass => $"The {creature} does not answer to your kind of summoner.",
                _ => $"The {creature} turns away from your Pact.",
            },
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
