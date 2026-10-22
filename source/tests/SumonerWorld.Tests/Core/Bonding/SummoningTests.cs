using System.Numerics;
using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Cognition.Lod;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.Transactions;
using SumonerWorld.Core.World;
using SumonerWorld.Scenarios;

namespace SumonerWorld.Tests.Core.Bonding;

public sealed class SummoningTests : IDisposable
{
    private const ulong Alice = 1001;
    private const ulong Bob = 1002;
    private static readonly Vector3 Player = new(100, 0, 100);

    private readonly CreatureWorld _world = new(ShippedData.Current, new CreatureWorldSettings(), seed: 4);
    private readonly SimulationScheduler _scheduler;
    private readonly TransactionProcessor _processor = new(new IntentDeduplicator());
    private readonly Entity _alice;
    private uint _sequence;

    public SummoningTests()
    {
        _scheduler = new SimulationScheduler(_world.Clock, _world.Events);
        _world.CreateSystems().RegisterAll(_scheduler);
        _alice = _world.SpawnPlayerAvatar(Alice, Player);
    }

    public void Dispose() => _world.Dispose();

    [Fact]
    public void A_summon_costs_mana_holds_the_player_in_a_sigil_and_then_brings_the_creature_to_their_side()
    {
        StableId fox = Pacted("bogfox", Player + new Vector3(300, 0, 0), Alice);
        int mana = Mana();

        Assert.True(Summon(fox).IsApplied);
        Assert.Equal(mana - _world.Data.Combat.SummonManaCost, Mana());
        Assert.Equal(CombatAction.Cast, CombatRules.ActionOf(_world.World, _world.Data, _alice, _world.Clock.Current.Index).Action);
        Assert.True(Bond(fox).Summoned);

        Run(_world.Data.Combat.SummonCastTicks + 1);

        Assert.False(_world.World.Has<SigilCast>(_alice), "the sigil is done");
        Assert.InRange(Distance(fox), 1.5f, 4f);
        Assert.Equal(Emote.Trust, _world.World.GetComponent<CurrentEmote>(Creature(fox)).Value);
    }

    [Fact]
    public void A_summoned_creature_is_always_at_the_full_ai_tier()
    {
        StableId fox = Pacted("bogfox", Player + new Vector3(300, 0, 0), Alice);
        Run(31);
        Assert.NotEqual(AiLodTier.Active, _world.World.GetComponent<AiTier>(Creature(fox)).Tier);

        _world.Services.Bonding.Restore(Mind(fox), Bond(fox) with { Summoned = true });
        Run(31);

        Assert.Equal(AiLodTier.Active, _world.World.GetComponent<AiTier>(Creature(fox)).Tier);
    }

    [Fact]
    public void A_rank_F_summoner_calls_one_companion_at_a_time_and_higher_ranks_more()
    {
        StableId first = Pacted("bogfox", Player + new Vector3(7, 0, 0), Alice);
        StableId second = Pacted("mossback", Player + new Vector3(8, 0, 0), Alice);

        Assert.True(Summon(first).IsApplied);
        Run(_world.Data.Combat.SummonCastTicks + 1);
        Assert.Equal((IntentRejectReason.WrongState, SummonRefusal.CapacityReached), Refusal(Summon(second)));
        Assert.Equal([1, 1, 2, 2, 3, 3, 4, 4, 5, 5], Enumerable.Range(0, 10).Select(_world.Data.Combat.BondCapacityOf));
    }

    [Fact]
    public void Summons_are_refused_for_wild_or_foreign_creatures_without_mana_beyond_bond_capacity_or_while_busy()
    {
        _world.World.GetComponent<SumonerWorld.Core.Progression.PlayerProgress>(_alice).Rank = (int)SumonerWorld.Core.Progression.Rank.D; // capacity 2
        StableId wild = Spawn("bogfox", Player + new Vector3(5, 0, 0));
        StableId bobs = Pacted("bogfox", Player + new Vector3(6, 0, 0), Bob);
        StableId first = Pacted("bogfox", Player + new Vector3(7, 0, 0), Alice);
        StableId second = Pacted("mossback", Player + new Vector3(8, 0, 0), Alice);
        StableId third = Pacted("mossback", Player + new Vector3(9, 0, 0), Alice);

        Assert.Equal((IntentRejectReason.NotOwner, SummonRefusal.NotPacted), Refusal(Summon(wild)));
        Assert.Equal((IntentRejectReason.NotOwner, SummonRefusal.NotPacted), Refusal(Summon(bobs)));

        Assert.True(Summon(first).IsApplied);
        Assert.Equal((IntentRejectReason.WrongState, SummonRefusal.Busy), Refusal(Summon(second)));
        Assert.Equal((IntentRejectReason.WrongState, SummonRefusal.AlreadySummoned), Refusal(Summon(first)));
        Run(_world.Data.Combat.SummonCastTicks + 1);

        Assert.True(Summon(second).IsApplied);
        Run(_world.Data.Combat.SummonCastTicks + 1);
        Assert.Equal(_world.Data.Combat.BondCapacityOf(_world.World.GetComponent<SumonerWorld.Core.Progression.PlayerProgress>(_alice).Rank), Companions.Summoned(_world, Alice));
        Assert.Equal((IntentRejectReason.WrongState, SummonRefusal.CapacityReached), Refusal(Summon(third)));

        Assert.True(Dismiss(first).IsApplied);
        _world.World.GetComponent<Combatant>(_alice) = _world.World.GetComponent<Combatant>(_alice) with { Mana = _world.Data.Combat.SummonManaCost - 1 };
        Assert.Equal((IntentRejectReason.WrongState, SummonRefusal.NoMana), Refusal(Summon(third)));
    }

    [Fact]
    public void Only_the_owner_can_dismiss_and_only_a_summoned_creature()
    {
        StableId fox = Pacted("bogfox", Player + new Vector3(5, 0, 0), Alice);
        Entity bob = _world.SpawnPlayerAvatar(Bob, Player + new Vector3(2, 0, 0));

        Assert.Equal(IntentRejectReason.WrongState, Dismiss(fox).Reason);
        Assert.True(Summon(fox).IsApplied);
        Assert.Equal(IntentRejectReason.NotOwner, Run(new DismissTransaction(_world, bob, fox)).Reason);

        Assert.True(Dismiss(fox).IsApplied);

        Assert.False(Bond(fox).Summoned);
        Assert.False(_world.World.Has<SigilCast>(_alice), "a dismissal during the cast ends it");
        Assert.Equal(OwnershipState.Pacted, Bond(fox).State);
    }

    [Fact]
    public void A_resent_summon_is_applied_once()
    {
        StableId fox = Pacted("bogfox", Player + new Vector3(5, 0, 0), Alice);
        int mana = Mana();
        var summon = new SummonTransaction(_world, _alice, fox);

        IntentResult first = _processor.Execute(new IntentId(1, 1), ref summon, _world.Clock.Current.Index);
        IntentResult again = _processor.Execute(new IntentId(1, 1), ref summon, _world.Clock.Current.Index);

        Assert.Equal(first, again);
        Assert.Equal(mana - _world.Data.Combat.SummonManaCost, Mana());
    }

    [Fact]
    public void The_companion_list_holds_the_players_pacted_creatures_in_id_order()
    {
        StableId b = Pacted("mossback", Player + new Vector3(8, 0, 0), Alice);
        StableId a = Pacted("bogfox", Player + new Vector3(7, 0, 0), Alice);
        Pacted("bogfox", Player + new Vector3(9, 0, 0), Bob);
        Spawn("bogfox", Player + new Vector3(10, 0, 0));
        var list = new List<Companion>();

        Companions.Of(_world, Alice, list);

        Assert.Equal([b, a], list.Select(companion => companion.Id));
        Assert.Equal(_world.SpeciesIndex("mossback"), list[0].Species);
        Assert.All(list, companion => Assert.False(companion.Summoned));
    }

    private static (IntentRejectReason, SummonRefusal) Refusal(IntentResult result) => (result.Reason, (SummonRefusal)result.Detail);

    private StableId Spawn(string species, Vector3 at) =>
        _world.World.GetComponent<StableId>(_world.SpawnCreature(_world.SpeciesIndex(species), at, new Personality(0, 0, 0, 0), group: 1));

    private StableId Pacted(string species, Vector3 at, ulong owner)
    {
        StableId id = Spawn(species, at);
        _world.Services.Bonding.Restore(Mind(id), new BondState(OwnershipState.Pacted, owner, PactTerms.Of(0), 0, 1, false, 0, 0, false));
        return id;
    }

    private Entity Creature(StableId id) => _world.TryFindCreature(id, out Entity creature) ? creature : throw new InvalidOperationException();

    private MindHandle Mind(StableId id) => _world.World.GetComponent<MindRef>(Creature(id)).Handle;

    private BondState Bond(StableId id) => _world.Services.Bonding.GetBond(Mind(id));

    private int Mana() => _world.World.GetComponent<Combatant>(_alice).Mana;

    private float Distance(StableId id) => Vector3.Distance(_world.World.GetComponent<Position>(Creature(id)).Value, Player);

    private IntentResult Summon(StableId id) => Run(new SummonTransaction(_world, _alice, id));

    private IntentResult Dismiss(StableId id) => Run(new DismissTransaction(_world, _alice, id));

    private IntentResult Run<T>(T transaction)
        where T : struct, ITransaction
    {
        return _processor.Execute(new IntentId(1, ++_sequence), ref transaction, _world.Clock.Current.Index);
    }

    private void Run(int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            _scheduler.RunTick();
        }
    }
}
