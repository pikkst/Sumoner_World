using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Items;
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

    /// <summary>A skill id as a name (until localization: <c>horn_toss</c> → <c>Horn toss</c>).</summary>
    public static string SkillName(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        string spaced = id.Replace('_', ' ');
        return spaced.Length == 0 ? spaced : char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }

    /// <summary>What a skill gives, in a few words: <c>new move: Lunge</c>, <c>health +10%</c>, <c>guard +2</c>.</summary>
    public static string SkillEffect(Core.Data.Definitions.SkillNodeDefinition node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.Move is string move)
        {
            return $"new move: {SkillName(move)}";
        }

        Core.Data.Definitions.SkillPassiveDefinition passive = node.Passive!;
        string percent = passive.Stat is Core.Data.Definitions.SkillStat.Health or Core.Data.Definitions.SkillStat.Power ? "%" : string.Empty;
        return $"{passive.Stat.ToString().ToLowerInvariant()} +{passive.Amount}{percent}";
    }

    /// <summary>A node's state in a few words (the panel and refusals).</summary>
    public static string SkillStateText(SkillState state) => state switch
    {
        SkillState.Learned => "learned",
        SkillState.Available => "can learn",
        SkillState.NeedsLevel => "needs a higher level",
        SkillState.NeedsBond => "needs a stronger bond",
        SkillState.NeedsStage => "needs to grow up",
        SkillState.NeedsSkill => "learn the skill before it first",
        SkillState.NoPoints => "no skill points",
        _ => "unknown skill",
    };

    /// <summary>How a creature feels about a branch, from its affinity.</summary>
    public static string BranchFeeling(int affinity) =>
        affinity >= SkillRules.WarmAt ? "likes it" : affinity <= SkillRules.ColdAt ? "dislikes it: skills cost 2" : "neutral";

    /// <summary>The player's level line in the HUD, e.g. <c>Elf · Rank F · Level 3</c> (M2-09).</summary>
    public static string PlayerLine(SummonerClass summonerClass, Rank rank, int level) => $"{summonerClass} · Rank {rank} · Level {level}";

    /// <summary>The line when a companion learned a combo with the player (M2-12): it is the player's style it learned.</summary>
    public static string ComboLearned(string creature, string combo) => $"Your {creature} learned your style: {SkillName(combo)}! It will join in when you cast it.";

    /// <summary>The gathering prompt for a node kind (M2-13).</summary>
    public static string GatherHint(string node) => node switch
    {
        "berry_bush" => "[H] pick berries",
        "sweetroot_patch" => "[H] dig up sweetroot",
        _ => "[H] gather branches",
    };

    /// <summary>An inventory line: the item's name and how many (M2-13).</summary>
    public static string ItemLine(string item, int count) => $"{SkillName(item)} x{count}";

    /// <summary>The window and bar keys (M2-14b), shown with the combat keys.</summary>
    public const string SpellHints = "1-0: action bar   L: abilities   I: bag   B: crafting   K: companion";

    /// <summary>The companion keys, shown under the companion list.</summary>
    public const string CompanionHints = "[Z] call   [X] send back\nCommands are on the action bar (7-0)";

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

        if (kind is IntentKind.Craft or IntentKind.Equip or IntentKind.Unequip or IntentKind.MoveItem or IntentKind.TransferItem or IntentKind.UseItem)
        {
            return GearResult(kind, result, creature);
        }

        if (kind == IntentKind.PickUp)
        {
            return result.IsApplied ? $"You pick up the loot (+{result.Detail} gold)."
                : (ItemRefusal)result.Detail == ItemRefusal.InventoryFull ? "Your bag is full." : string.Empty;
        }

        if (kind == IntentKind.Gather)
        {
            return result.IsApplied ? $"You gather {result.Detail} {creature}."
                : result.Reason == IntentRejectReason.OutOfRange ? "Come closer to gather."
                : (ItemRefusal)result.Detail == ItemRefusal.InventoryFull ? "Your bag is full."
                : "Nothing to gather here yet. It grows back by tomorrow.";
        }

        if (kind == IntentKind.Cast)
        {
            return result.IsApplied ? $"{creature}!" : result.Reason switch
            {
                IntentRejectReason.WrongState => "Not enough mana, or busy.",
                IntentRejectReason.OnCooldown => "Not so fast.",
                _ => "Nothing happens.",
            };
        }

        if (kind == IntentKind.LearnSkill)
        {
            return result.IsApplied ? $"The {creature} learns a new skill." : $"Not yet: {SkillStateText((SkillState)result.Detail)}.";
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

    /// <summary>The line for a craft, equip, unequip or bag result; <paramref name="name"/> is the recipe's or the item's name (M2-14, M2-14b). Moves that worked say nothing: the bag shows it.</summary>
    public static string GearResult(IntentKind kind, IntentResult result, string name)
    {
        if (result.IsApplied)
        {
            return kind switch
            {
                IntentKind.Craft => $"You start making: {name}. Stand still, a hit will stop you.",
                IntentKind.Equip => $"You put on: {name}.",
                IntentKind.Unequip => $"You take off: {name}.",
                IntentKind.UseItem => $"You use: {name}.",
                _ => string.Empty,
            };
        }

        return result.Reason switch
        {
            IntentRejectReason.InvalidTarget => "Call that companion to your side first.",
            IntentRejectReason.WrongState => (GearRefusal)result.Detail switch
            {
                GearRefusal.MissingInputs => $"You do not have everything for {name}.",
                GearRefusal.InventoryFull => "Your bag is full.",
                GearRefusal.NeedsStation => "That needs a workbench or a forge.",
                GearRefusal.Busy => "Not now: you are busy.",
                GearRefusal.CannotWear => $"{name} cannot be worn by that one.",
                GearRefusal.LevelTooLow => $"Not strong enough for {name} yet.",
                GearRefusal.NotUsable => $"{name} cannot be used.",
                GearRefusal.NotWithYou => "Call that companion to your side first.",
                GearRefusal.Down => "Too late: they are down.",
                _ => "Nothing happens.",
            },
            _ => "Nothing happens.",
        };
    }

    /// <summary>A recipe line in the crafting panel: what it makes and from what, e.g. <c>Wooden staff  ← 3 Fallen branch</c>.</summary>
    public static string RecipeLine(Core.Data.Definitions.RecipeDefinition recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        string output = recipe.Output.Count > 1 ? $"{SkillName(recipe.Output.Item)} x{recipe.Output.Count}" : SkillName(recipe.Output.Item);
        return $"{output}  ← {string.Join(", ", recipe.Inputs.Select(part => $"{part.Count} {SkillName(part.Item)}"))}";
    }

    /// <summary>The line when crafting ended: made, or stopped (a hit, a move, or the inputs went missing).</summary>
    public static string CraftEnded(string name, bool made) => made ? $"You made: {name}." : $"You stopped making: {name}. Nothing was used up.";

    /// <summary>An item's tooltip: its name, what it is and what it gives (M2-14b).</summary>
    public static string ItemTooltip(Core.Data.Definitions.ItemDefinition item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var lines = new List<string> { SkillName(item.Id), item.Category.ToString().ToLowerInvariant() };
        Core.Data.Definitions.GearStats stats = item.Stats;
        var gives = new List<string>();
        AddIf(gives, stats.Power, "power +{0}%");
        AddIf(gives, stats.Guard, "guard +{0}");
        AddIf(gives, stats.ResistPhysical / 10, "physical resist +{0}%");
        AddIf(gives, stats.ResistPrimal / 10, "primal resist +{0}%");
        AddIf(gives, stats.Stamina, "stamina +{0}");
        AddIf(gives, item.Restore.Health, "restores {0} health");
        AddIf(gives, item.Restore.Stamina, "restores {0} stamina");
        AddIf(gives, item.Restore.Mana, "restores {0} mana");
        lines.AddRange(gives);
        if (item.Slot != Core.Data.Definitions.EquipSlot.None)
        {
            lines.Add($"worn by: {string.Join(", ", item.Wearers)}" + (item.MinLevel > 1 ? $" (level {item.MinLevel}+)" : string.Empty));
        }

        lines.Add(item.Restore.IsUsable ? "right-click: use" : item.Slot != Core.Data.Definitions.EquipSlot.None ? "right-click: put on" : string.Empty);
        return string.Join('\n', lines.Where(line => line.Length > 0));
    }

    private static void AddIf(List<string> lines, int value, string format)
    {
        if (value > 0)
        {
            lines.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, format, value));
        }
    }

    /// <summary>An action bar button's name: the spell, item or skill, or the command as the player calls it (M2-14b).</summary>
    public static string ActionName(ActionRef action) => action.Kind switch
    {
        ActionKind.None => string.Empty,
        ActionKind.Command => action.Id switch
        {
            nameof(CompanionCommand.Attack) => "Attack!",
            nameof(CompanionCommand.GuardMe) => "Guard me",
            nameof(CompanionCommand.Hold) => "Hold here",
            nameof(CompanionCommand.Retreat) => "Come back",
            _ => action.Id,
        },
        _ => SkillName(action.Id),
    };

    /// <summary>The name of an equipment position.</summary>
    public static string PositionName(EquipPosition position) => position switch
    {
        EquipPosition.OffHand => "off hand",
        EquipPosition.Charm1 or EquipPosition.Charm2 => "charm",
        _ => position.ToString().ToLowerInvariant(),
    };

    /// <summary>A species id as a display name (until localization: <c>hollow_owl</c> → <c>hollow owl</c>).</summary>
    public static string DisplayName(string speciesId)
    {
        ArgumentNullException.ThrowIfNull(speciesId);
        return speciesId.Replace('_', ' ');
    }
}
