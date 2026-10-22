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

    /// <summary>The name shown for a player until players choose names: <c>Summoner 1002</c> (M2-20).</summary>
    public static string PlayerName(ulong account) => $"Summoner {account}";

    /// <summary>A player's nameplate above their head: name, class, rank and level (M2-20).</summary>
    public static string Nameplate(RosterEntry profile) =>
        $"{PlayerName(profile.Account)}\n{PlayerLine((SummonerClass)profile.Class, (Rank)profile.Rank, profile.Level)}";

    /// <summary>
    /// The player's own profile lines (M2-20): class, rank and level with the rank's level cap, experience to the next level and
    /// Bond Capacity.
    /// </summary>
    public static string ProfileLines(RosterEntry profile, GrowthRules growth, Core.Data.Definitions.CombatDefinition combat)
    {
        ArgumentNullException.ThrowIfNull(growth);
        ArgumentNullException.ThrowIfNull(combat);
        int cap = GrowthRules.Cap((Rank)profile.Rank);
        string xp = profile.Level >= cap
            ? $"XP {profile.Xp} (level cap {cap}: rank up to grow further)"
            : $"XP {profile.Xp} / {growth.XpFor(profile.Level + 1)} for level {profile.Level + 1}";
        return $"{PlayerLine((SummonerClass)profile.Class, (Rank)profile.Rank, profile.Level)} (cap {cap})\n{xp}\n"
            + $"Bond Capacity {combat.BondCapacityOf(profile.Rank)}: companions you can call at once";
    }

    /// <summary>The line when a companion learned a combo with the player (M2-12): it is the player's style it learned.</summary>
    public static string ComboLearned(string creature, string combo) => $"Your {creature} learned your style: {SkillName(combo)}! It will join in when you cast it.";

    /// <summary>The gathering prompt for a node kind (M2-13).</summary>
    public static string GatherHint(string node) => node switch
    {
        "berry_bush" => "[H] pick berries",
        "sweetroot_patch" => "[H] dig up sweetroot",
        "glowcap_cluster" => "[H] pick glowcaps",
        "ore_vein" => "[H] chip off ore",
        _ => "[H] gather branches",
    };

    /// <summary>An inventory line: the item's name and how many (M2-13).</summary>
    public static string ItemLine(string item, int count) => $"{SkillName(item)} x{count}";

    /// <summary>A magic school's name, e.g. <c>Storm</c> for <c>storm</c> (E3a).</summary>
    public static string SchoolName(string school) => school switch
    {
        "primal" => "Primal",
        "storm" => "Storm",
        "umbral" => "Umbral",
        "ember" => "Ember",
        _ => school,
    };

    /// <summary>The line after a command was given (the companions answer with their bodies).</summary>
    public static string CommandSent(CompanionCommand command) => command switch
    {
        CompanionCommand.Attack => "You call: attack!",
        CompanionCommand.GuardMe => "You call: guard me!",
        CompanionCommand.Hold => "You call: hold here!",
        CompanionCommand.Retreat => "You call: come back!",
        _ => string.Empty,
    };

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
        if (result.Reason == IntentRejectReason.WrongState && result.Detail == Core.Academy.ArenaRules.SafeZoneDetail && kind is IntentKind.Cast or IntentKind.Hit or IntentKind.Attack)
        {
            return "No fighting inside the Academy. The arena is just inside the gate.";
        }

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

        if (kind is IntentKind.Buy or IntentKind.Sell or IntentKind.Rest)
        {
            return TradeResult(kind, result, creature);
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

        if (Later(kind, result, creature, argument) is string line)
        {
            return line;
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

    /// <summary>The line for a trade or a rest (E5b); <paramref name="name"/> is what changed hands, e.g. <c>3 Berry meal</c>.</summary>
    public static string TradeResult(IntentKind kind, IntentResult result, string name)
    {
        if (result.IsApplied)
        {
            return kind switch
            {
                IntentKind.Buy => $"You buy {name}.",
                IntentKind.Sell => $"You sell {name}.",
                _ => "You rest by the fire. Health, stamina and mana are full again.",
            };
        }

        return result.Reason == IntentRejectReason.OutOfRange ? "Come closer to trade." : (Core.Items.TradeRefusal)result.Detail switch
        {
            Core.Items.TradeRefusal.NotEnoughGold => "You do not have enough gold.",
            Core.Items.TradeRefusal.BagFull => "Your bag is full.",
            Core.Items.TradeRefusal.DoesNotBuy => $"They do not buy {name}.",
            Core.Items.TradeRefusal.NotForSale => "That is not for sale.",
            _ => "Nothing happens.",
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

    // The lines of the intents added since M2-08 (commands, companion skills, quests, the Academy), or null for the older ones.
    private static string? Later(IntentKind kind, IntentResult result, string creature, byte argument)
    {
        if (kind == IntentKind.Read)
        {
            return result.IsApplied
                ? result.Detail == 1 ? "Yes! You read it right." : "Not quite. Look again: watch its ears, its tail and how it moves."
                : result.Reason == IntentRejectReason.OutOfRange ? "That creature is too far away to read." : "Choose a creature first: lock on with T.";
        }

        if (kind is IntentKind.Talk or IntentKind.Answer)
        {
            return result.IsApplied ? string.Empty : result.Reason == IntentRejectReason.OutOfRange ? "Come closer to talk." : string.Empty;
        }

        if (kind is IntentKind.AcceptQuest or IntentKind.AbandonQuest)
        {
            return QuestResult(kind, result, creature);
        }

        if (kind == IntentKind.CompanionSkill)
        {
            return result.IsApplied ? $"You call for {creature}!" : result.Reason switch
            {
                IntentRejectReason.WrongState when result.Detail == 1 => $"No companion at your side knows {creature}.",
                IntentRejectReason.WrongState => "No companion is with you. Call one first [Z].",
                IntentRejectReason.InvalidTarget => "Choose a creature to attack: lock on with T, or stand near it.",
                IntentRejectReason.OutOfRange => "That is too far away.",
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

        return null;
    }

    /// <summary>The line for taking or leaving a quest; <paramref name="title"/> is the quest's title (M2-19).</summary>
    public static string QuestResult(IntentKind kind, IntentResult result, string title)
    {
        if (result.IsApplied)
        {
            return kind == IntentKind.AcceptQuest ? $"New quest: {title}." : $"You leave the quest: {title}.";
        }

        return (Core.Quests.QuestRefusal)result.Detail switch
        {
            Core.Quests.QuestRefusal.AlreadyTaken => "You have that quest already.",
            Core.Quests.QuestRefusal.LevelTooLow => "You need a higher level for that quest.",
            Core.Quests.QuestRefusal.NeedsEarlierQuest => "Finish the quest before it first.",
            Core.Quests.QuestRefusal.TooMany => "You have too many quests. Finish or leave one first.",
            Core.Quests.QuestRefusal.OtherClass => "That quest is for summoners of another tradition.",
            _ => "Nothing happens.",
        };
    }

    /// <summary>An objective with its progress, e.g. <c>Defeat bogfox 1/2</c> or <c>Reach the north clearing (done)</c> (M2-19).</summary>
    public static string ObjectiveLine(Core.Data.Definitions.QuestObjectiveDefinition objective, int count)
    {
        ArgumentNullException.ThrowIfNull(objective);
        string target = objective.Target.Length == 0 ? "any creature" : DisplayName(objective.Target);
        string what = objective.Type switch
        {
            Core.Data.Definitions.ObjectiveType.Defeat => $"Defeat {target}",
            Core.Data.Definitions.ObjectiveType.Gather => $"Gather {(objective.Target.Length == 0 ? "anything" : DisplayName(objective.Target))}",
            Core.Data.Definitions.ObjectiveType.Craft => $"Make {(objective.Target.Length == 0 ? "anything" : DisplayName(objective.Target))}",
            Core.Data.Definitions.ObjectiveType.Pact => $"Make a Pact with {target}",
            Core.Data.Definitions.ObjectiveType.Feed => $"Feed {target}",
            Core.Data.Definitions.ObjectiveType.Help => $"Free {target} from a snare",
            Core.Data.Definitions.ObjectiveType.Talk => objective.Target == Core.Data.Definitions.AcademyDefinition.OwnKeeper ? "Talk to your Keeper" : $"Talk to {TitleCase(objective.Target)}",
            Core.Data.Definitions.ObjectiveType.Answer => "Answer the questions",
            Core.Data.Definitions.ObjectiveType.Read => "Tell how a creature feels",
            Core.Data.Definitions.ObjectiveType.Offer => $"Offer a Pact to {target}",
            Core.Data.Definitions.ObjectiveType.Spare => $"Beat {target} until it yields, then let it go",
            _ => $"Go to {TitleCase(objective.Place)}",
        };
        return objective.Type == Core.Data.Definitions.ObjectiveType.Reach
            ? what + (count > 0 ? " (done)" : string.Empty)
            : $"{what} {Math.Min(count, objective.Count)}/{objective.Count}";
    }

    // An id as a name: scout_rook → Scout Rook.
    private static string TitleCase(string id) =>
        string.Join(' ', id.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(word => char.ToUpperInvariant(word[0]) + word[1..]));

    /// <summary>A quest's rewards in a few words, e.g. <c>40 XP, 10 gold, Berry meal x2</c> (M2-19).</summary>
    public static string RewardsLine(Core.Data.Definitions.QuestRewardsDefinition rewards)
    {
        ArgumentNullException.ThrowIfNull(rewards);
        var parts = new List<string>();
        if (rewards.Xp > 0)
        {
            parts.Add($"{rewards.Xp} XP");
        }

        if (rewards.Gold > 0)
        {
            parts.Add($"{rewards.Gold} gold");
        }

        parts.AddRange(rewards.Items.Select(item => ItemLine(item.Item, item.Count)));
        return string.Join(", ", parts);
    }

    /// <summary>How far and which way an offset on the ground lies (+z is north, +x east), e.g. <c>40 m north-east</c> (M2-16).</summary>
    public static string Direction(float dx, float dz)
    {
        float distance = MathF.Sqrt((dx * dx) + (dz * dz));
        if (distance < 3)
        {
            return "here";
        }

        string[] ways = ["north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west"];
        float bearing = MathF.Atan2(dx, dz) * 180 / MathF.PI;
        int sector = (int)MathF.Round(((bearing % 360) + 360) % 360 / 45) % 8;
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{MathF.Round(distance / 5) * 5:0} m {ways[sector]}");
    }

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
