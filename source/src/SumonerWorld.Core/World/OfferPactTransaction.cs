using System.Numerics;
using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecosystem;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Transactions;

namespace SumonerWorld.Core.World;

/// <summary>
/// A player offers a Pact to a wild creature (AI Bible §10.2), run through the <see cref="ITransactionProcessor"/>. Validation: the
/// player's avatar exists, the terms are in the catalogue, the creature exists and is wild, it is within <see cref="Range"/> and the
/// player's offer cooldown has passed. The commit asks the creature (acceptance uses the player's reputation with its kind in the
/// region); a refusal is still applied, because the creature remembers being asked. <see cref="IntentResult.Detail"/> is the
/// <see cref="BondingOutcome"/>.
/// </summary>
/// <param name="Creatures">The world.</param>
/// <param name="Cooldowns">Per-player cooldowns.</param>
/// <param name="Avatar">The offering player's avatar.</param>
/// <param name="Creature">The creature, by stable id (what clients name it by).</param>
/// <param name="Terms">The offered Pact Terms.</param>
public record struct OfferPactTransaction(CreatureWorld Creatures, CooldownTracker Cooldowns, Entity Avatar, StableId Creature, PactTerms Terms)
    : ITransaction
{
    /// <summary>Intent kind id for cooldowns.</summary>
    public const ushort Kind = 1;

    /// <summary>Farthest distance to the creature, in metres.</summary>
    public const float Range = 10f;

    /// <summary>One offer per second per player.</summary>
    public const ulong CooldownTicks = 30;

    /// <inheritdoc />
    public readonly IntentResult Validate(ulong now)
    {
        ArgumentNullException.ThrowIfNull(Creatures);
        ArgumentNullException.ThrowIfNull(Cooldowns);
        EcsWorld world = Creatures.World;
        if (!world.Store<PlayerAvatar>().Has(Avatar))
        {
            return IntentResult.Reject(IntentRejectReason.InvalidTarget);
        }

        if (Terms.Mask == 0 || Terms.Mask >> Creatures.Data.PactTerms.Count != 0)
        {
            return IntentResult.Reject(IntentRejectReason.InvalidArguments);
        }

        if (!Creatures.TryFindCreature(Creature, out Entity creature))
        {
            return IntentResult.Reject(IntentRejectReason.InvalidTarget);
        }

        if (Creatures.Services.Bonding.GetBond(world.GetComponent<MindRef>(creature).Handle).State != OwnershipState.Wild)
        {
            return IntentResult.Reject(IntentRejectReason.WrongState);
        }

        if (Vector3.Distance(world.GetComponent<Position>(Avatar).Value, world.GetComponent<Position>(creature).Value) > Range)
        {
            return IntentResult.Reject(IntentRejectReason.OutOfRange);
        }

        return Cooldowns.IsReady(world.GetComponent<PlayerAvatar>(Avatar).AccountId, Kind, now)
            ? IntentResult.Applied()
            : IntentResult.Reject(IntentRejectReason.OnCooldown);
    }

    /// <inheritdoc />
    public readonly IntentResult Commit(ulong now)
    {
        EcsWorld world = Creatures.World;
        Creatures.TryFindCreature(Creature, out Entity creature);
        ulong account = world.GetComponent<PlayerAvatar>(Avatar).AccountId;
        ushort species = world.GetComponent<SpeciesRef>(creature).Index;
        Vector3 position = world.GetComponent<Position>(creature).Value;
        int reputation = Creatures.Reputation.GetScore(new ReputationKey(species, Creatures.RegionOf(position), account));

        BondingOutcome outcome = Creatures.Services.Bonding.OfferPact(
            world.GetComponent<MindRef>(creature).Handle,
            Creatures.Data.Species[species],
            new MemorySubject(SubjectKind.Player, account),
            Terms,
            reputation,
            now,
            out _);
        Cooldowns.Start(account, Kind, now, CooldownTicks);
        return IntentResult.Applied((byte)outcome);
    }
}
