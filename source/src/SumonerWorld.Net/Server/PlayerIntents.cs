using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Items;
using SumonerWorld.Core.Progression;
using SumonerWorld.Core.Transactions;
using SumonerWorld.Core.World;
using SumonerWorld.Net.Messages;

namespace SumonerWorld.Net.Server;

/// <summary>
/// Turns a player's intent into its transaction and runs it through the <see cref="TransactionProcessor"/> (TDD §3.2, R27, R34),
/// with the per-player cooldowns. <c>changed</c> is the creature the journal saves right away.
/// </summary>
internal sealed class PlayerIntents(CreatureWorld world, TransactionProcessor transactions)
{
    private readonly CooldownTracker _cooldowns = new();

    /// <summary>Runs one intent of the player whose avatar is <paramref name="avatar"/>.</summary>
    public IntentResult Execute(IntentId id, Entity avatar, IntentRequest request, ulong now, out Entity? changed)
    {
        changed = null;
        var target = new StableId(request.Target);
        switch (request.Kind)
        {
            case IntentKind.OfferPact:
                var offer = new OfferPactTransaction(world, _cooldowns, avatar, target, new PactTerms(request.Argument));
                return Changed(transactions.Execute(id, ref offer, now), target, out changed);
            case IntentKind.Feed:
                var feed = new FeedTransaction(world, _cooldowns, avatar, target);
                return Changed(transactions.Execute(id, ref feed, now), target, out changed);
            case IntentKind.Hit:
                var hit = new HitTransaction(world, avatar, target);
                return Changed(transactions.Execute(id, ref hit, now), target, out changed);
            case IntentKind.Help:
                var help = new HelpTransaction(world, _cooldowns, avatar, target);
                return Changed(transactions.Execute(id, ref help, now), target, out changed);
            case IntentKind.Scare:
                var scare = new ScareTransaction(world, _cooldowns, avatar);
                return transactions.Execute(id, ref scare, now);
            case IntentKind.Summon:
                var summon = new SummonTransaction(world, avatar, target);
                return Changed(transactions.Execute(id, ref summon, now), target, out changed);
            case IntentKind.Dismiss:
                var dismiss = new DismissTransaction(world, avatar, target);
                return Changed(transactions.Execute(id, ref dismiss, now), target, out changed);
            case IntentKind.PickUp or IntentKind.Gather or IntentKind.Craft or IntentKind.Equip or IntentKind.Unequip
                or IntentKind.MoveItem or IntentKind.TransferItem or IntentKind.UseItem:
                IntentResult item = ExecuteItem(id, avatar, request, now);
                return request.Kind is IntentKind.Equip or IntentKind.Unequip or IntentKind.TransferItem or IntentKind.UseItem
                    ? Changed(item, target, out changed)
                    : item;
            case IntentKind.Cast:
                var cast = new CastTransaction(
                    world, avatar, request.Argument, (byte)(request.Target & 0xFF), ((request.Target >> 8) & 0xFFFF) / 10f, new StableId(request.Target >> 24));
                return transactions.Execute(id, ref cast, now);
            case IntentKind.LearnSkill:
                var learn = new LearnSkillTransaction(world, avatar, target, request.Argument);
                return Changed(transactions.Execute(id, ref learn, now), target, out changed);
            case IntentKind.Talk when request.Target <= byte.MaxValue:
                var talk = new Core.Academy.TalkTransaction(world, avatar, (int)request.Target);
                return transactions.Execute(id, ref talk, now);
            case IntentKind.Answer when request.Target <= byte.MaxValue:
                var answer = new Core.Academy.AnswerTransaction(world, avatar, (int)request.Target, request.Argument / 4, request.Argument % 4);
                return transactions.Execute(id, ref answer, now);
            case IntentKind.AcceptQuest:
                var accept = new Core.Quests.AcceptQuestTransaction(world, avatar, request.Argument);
                return transactions.Execute(id, ref accept, now);
            case IntentKind.AbandonQuest:
                var abandon = new Core.Quests.AbandonQuestTransaction(world, avatar, request.Argument);
                return transactions.Execute(id, ref abandon, now);
            case IntentKind.CompanionSkill:
                var skill = new SkillCommandTransaction(world, avatar, target, request.Argument);
                return transactions.Execute(id, ref skill, now);
            case IntentKind.Command:
                var command = new CommandTransaction(world, avatar, (CompanionCommand)request.Argument, target);
                return transactions.Execute(id, ref command, now);
            case IntentKind.Attack when request.Target <= byte.MaxValue:
                var attack = new AttackTransaction(world, avatar, request.Argument, (byte)request.Target);
                return transactions.Execute(id, ref attack, now);
            default:
                return IntentResult.Reject(IntentRejectReason.InvalidArguments);
        }
    }

    // Items, crafting and gear (M2-13, M2-14, M2-14b).
    private IntentResult ExecuteItem(IntentId id, Entity avatar, IntentRequest request, ulong now)
    {
        var target = new StableId(request.Target);
        switch (request.Kind)
        {
            case IntentKind.PickUp:
                var pickUp = new PickUpTransaction(world, avatar, request.Target);
                return transactions.Execute(id, ref pickUp, now);
            case IntentKind.Gather:
                var gather = new GatherTransaction(world, avatar, request.Target);
                return transactions.Execute(id, ref gather, now);
            case IntentKind.Craft:
                var craft = new CraftTransaction(world, avatar, request.Argument);
                return transactions.Execute(id, ref craft, now);
            case IntentKind.Equip:
                var equip = new EquipTransaction(world, avatar, target, request.Argument);
                return transactions.Execute(id, ref equip, now);
            case IntentKind.Unequip:
                var unequip = new UnequipTransaction(world, avatar, target, (EquipPosition)request.Argument);
                return transactions.Execute(id, ref unequip, now);
            case IntentKind.MoveItem when request.Target <= byte.MaxValue:
                var move = new MoveItemTransaction(world, avatar, (int)request.Target, request.Argument);
                return transactions.Execute(id, ref move, now);
            case IntentKind.TransferItem:
                bool fromCreature = (request.Argument & IntentArguments.FromCreature) != 0;
                var transfer = new TransferItemTransaction(world, avatar, target, request.Argument & ~IntentArguments.FromCreature, fromCreature);
                return transactions.Execute(id, ref transfer, now);
            case IntentKind.UseItem:
                var use = new UseItemTransaction(world, avatar, target, request.Argument);
                return transactions.Execute(id, ref use, now);
            default:
                return IntentResult.Reject(IntentRejectReason.InvalidArguments);
        }
    }

    /// <summary>Drops a player's cooldowns when their session ends.</summary>
    public void Forget(ulong account) => _cooldowns.Forget(account);

    // The journal saves the target creature right away; creatures that died, and witnesses, wait for the next checkpoint.
    private IntentResult Changed(IntentResult result, StableId target, out Entity? changed)
    {
        changed = world.TryFindCreature(target, out Entity creature) ? creature : null;
        return result;
    }
}
