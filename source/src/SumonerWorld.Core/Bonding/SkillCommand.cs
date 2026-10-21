using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Progression;
using SumonerWorld.Core.Transactions;
using SumonerWorld.Core.World;

namespace SumonerWorld.Core.Bonding;

/// <summary>
/// A move the summoner asked a companion for (M2-14c): it tries this move first when it attacks, until it has used it or
/// <see cref="Until"/> passes. No padding (hashed raw).
/// </summary>
/// <param name="Until">Last tick it still tries.</param>
/// <param name="Move">Move index.</param>
/// <param name="Reserved">Padding, always 0.</param>
public record struct RequestedMove(ulong Until, int Move, int Reserved);

/// <summary>
/// A summoner calls for one of their companions' learned moves on a target (companions spec §8, M2-14c: a companion skill on the
/// action bar). It is an Attack command (every companion at their side is asked, and answers by its obedience), and those that
/// know the move and do not refuse try it first. Validation is the Attack's, plus: the move must be one a companion at their
/// side has learned and has a slot for.
/// </summary>
/// <param name="Creatures">The world.</param>
/// <param name="Avatar">The summoner's avatar.</param>
/// <param name="Target">The target's stable id.</param>
/// <param name="Move">The move index.</param>
public record struct SkillCommandTransaction(CreatureWorld Creatures, Entity Avatar, StableId Target, int Move) : ITransaction
{
    /// <summary>How long a companion keeps trying the asked-for move, in ticks (5 seconds).</summary>
    public const ulong TryTicks = 150;

    /// <inheritdoc />
    public readonly IntentResult Validate(ulong now)
    {
        ArgumentNullException.ThrowIfNull(Creatures);
        if (!Creatures.World.Store<PlayerAvatar>().Has(Avatar))
        {
            return IntentResult.Reject(IntentRejectReason.InvalidTarget);
        }

        if ((uint)Move >= (uint)Creatures.Data.Moves.Count)
        {
            return IntentResult.Reject(IntentRejectReason.InvalidArguments);
        }

        IntentResult attack = Attack().Validate(now);
        if (!attack.IsApplied)
        {
            return attack;
        }

        ulong account = Creatures.World.GetComponent<PlayerAvatar>(Avatar).AccountId;
        ComponentStore<MindRef> minds = Creatures.World.Store<MindRef>();
        ReadOnlySpan<Entity> creatures = minds.Entities;
        for (int i = 0; i < creatures.Length; i++)
        {
            if (IsSummonedBy(minds.Components[i].Handle, account) && Knows(creatures[i]))
            {
                return IntentResult.Applied();
            }
        }

        return new IntentResult(IntentStatus.Rejected, IntentRejectReason.WrongState, 1);
    }

    /// <inheritdoc />
    public readonly IntentResult Commit(ulong now)
    {
        IntentResult result = Attack().Commit(now);
        ulong account = Creatures.World.GetComponent<PlayerAvatar>(Avatar).AccountId;
        ComponentStore<MindRef> minds = Creatures.World.Store<MindRef>();
        ReadOnlySpan<Entity> creatures = minds.Entities;
        for (int i = 0; i < creatures.Length; i++)
        {
            Entity companion = creatures[i];
            if (!IsSummonedBy(minds.Components[i].Handle, account) || !Knows(companion)
                || Creatures.World.GetComponent<CompanionOrder>(companion).Response == (int)CommandResponse.Refuse)
            {
                continue;
            }

            var request = new RequestedMove(now + TryTicks, Move, 0);
            if (Creatures.World.Has<RequestedMove>(companion))
            {
                Creatures.World.GetComponent<RequestedMove>(companion) = request;
            }
            else
            {
                Creatures.World.Add(companion, request);
            }
        }

        return result;
    }

    /// <summary>True when the creature has learned <paramref name="move"/> and it is one of its active skills.</summary>
    public static bool KnowsMove(CreatureWorld world, Entity creature, int move)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!world.World.Has<CreatureSkills>(creature) || !world.World.Has<CreatureGrowth>(creature))
        {
            return false;
        }

        Span<int> moves = stackalloc int[SkillRules.Slots(LifeStage.Elder)];
        int count = SkillRules.ActiveMoves(
            world.Data,
            world.Data.SkillTreeOf(world.World.GetComponent<SpeciesRef>(creature).Index),
            world.World.GetComponent<CreatureSkills>(creature).Learned,
            (LifeStage)world.World.GetComponent<CreatureGrowth>(creature).Stage,
            moves);
        return moves[..count].Contains(move);
    }

    private readonly CommandTransaction Attack() => new(Creatures, Avatar, CompanionCommand.Attack, Target);

    private readonly bool Knows(Entity creature) => KnowsMove(Creatures, creature, Move);

    private readonly bool IsSummonedBy(Cognition.Memory.MindHandle mind, ulong account)
    {
        BondState bond = Creatures.Services.Bonding.GetBond(mind);
        return bond.Summoned && bond.Owner == account;
    }
}
