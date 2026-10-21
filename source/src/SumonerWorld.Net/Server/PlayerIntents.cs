using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Intents;
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
            case IntentKind.Cast:
                var cast = new CastTransaction(
                    world, avatar, request.Argument, (byte)(request.Target & 0xFF), ((request.Target >> 8) & 0xFFFF) / 10f, new StableId(request.Target >> 24));
                return transactions.Execute(id, ref cast, now);
            case IntentKind.LearnSkill:
                var learn = new LearnSkillTransaction(world, avatar, target, request.Argument);
                return Changed(transactions.Execute(id, ref learn, now), target, out changed);
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

    /// <summary>Drops a player's cooldowns when their session ends.</summary>
    public void Forget(ulong account) => _cooldowns.Forget(account);

    // The journal saves the target creature right away; creatures that died, and witnesses, wait for the next checkpoint.
    private IntentResult Changed(IntentResult result, StableId target, out Entity? changed)
    {
        changed = world.TryFindCreature(target, out Entity creature) ? creature : null;
        return result;
    }
}
