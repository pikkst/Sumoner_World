using System.Numerics;
using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Transactions;
using SumonerWorld.Core.World;
using SumonerWorld.Scenarios;

namespace SumonerWorld.Tests.Core.World;

public sealed class OfferPactTransactionTests : IDisposable
{
    private const ulong Account = 42;

    private readonly CreatureWorld _world = new(ShippedData.Current, new CreatureWorldSettings(), seed: 7);
    private readonly TransactionProcessor _processor = new(new IntentDeduplicator());
    private readonly CooldownTracker _cooldowns = new();
    private readonly Entity _avatar;
    private readonly StableId _creature;

    public OfferPactTransactionTests()
    {
        ushort species = _world.SpeciesIndex("mossback");
        _avatar = _world.SpawnPlayerAvatar(Account, new Vector3(100, 0, 100));
        Entity creature = _world.SpawnCreature(
            species, new Vector3(105, 0, 100), Personality.Roll(_world.Data.Species[species], _world.Random), group: 0);
        _creature = _world.World.GetComponent<StableId>(creature);
    }

    public void Dispose() => _world.Dispose();

    [Fact]
    public void An_offer_in_range_is_applied_once_however_often_it_is_resent()
    {
        int memoriesBefore = _world.Minds.TotalRecords;

        IntentResult first = Offer(sequence: 1, now: 0);
        int memoriesAfter = _world.Minds.TotalRecords;
        IntentResult resent = Offer(sequence: 1, now: 5);
        Offer(sequence: 1, now: 6);

        Assert.True(first.IsApplied);
        Assert.Contains((BondingOutcome)first.Detail, new[] { BondingOutcome.Accepted, BondingOutcome.Refused });
        Assert.Equal(first, resent);
        Assert.True(memoriesAfter > memoriesBefore, "The creature remembers being asked.");
        Assert.Equal(memoriesAfter, _world.Minds.TotalRecords);
        Assert.Equal(2, _processor.Duplicates);
    }

    [Fact]
    public void Offers_are_rejected_out_of_range_on_cooldown_or_for_unknown_creatures_and_bad_terms()
    {
        Assert.Equal(IntentRejectReason.InvalidTarget, Offer(1, 0, new StableId(999_999)).Reason);
        Assert.Equal(IntentRejectReason.InvalidArguments, Offer(2, 0, terms: PactTerms.None).Reason);
        Assert.Equal(IntentRejectReason.InvalidArguments, Offer(3, 0, terms: new PactTerms(0x80)).Reason);

        Assert.True(Offer(4, now: 0).IsApplied);
        IntentResult tooSoon = Offer(5, now: OfferPactTransaction.CooldownTicks - 1);

        _world.World.GetComponent<Position>(_avatar) = new Position(new Vector3(100, 0, 120));
        IntentResult tooFar = Offer(6, now: 1000);

        Assert.Equal(IntentRejectReason.OnCooldown, tooSoon.Reason);
        Assert.Equal(IntentRejectReason.OutOfRange, tooFar.Reason);
    }

    private IntentResult Offer(uint sequence, ulong now, StableId? creature = null, PactTerms? terms = null)
    {
        var transaction = new OfferPactTransaction(_world, _cooldowns, _avatar, creature ?? _creature, terms ?? PactTerms.Of(0));
        return _processor.Execute(new IntentId(1, sequence), ref transaction, now);
    }
}
