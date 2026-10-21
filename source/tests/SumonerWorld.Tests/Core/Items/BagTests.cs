using System.Numerics;
using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Items;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.Transactions;
using SumonerWorld.Core.World;
using SumonerWorld.Scenarios;

namespace SumonerWorld.Tests.Core.Items;

public sealed class BagTests : IDisposable
{
    private const ulong Alice = 1001;
    private const ulong Bob = 1002;
    private static readonly Vector3 Start = new(100, 0, 100);

    private readonly CreatureWorld _world = new(ShippedData.Current, new CreatureWorldSettings(), seed: 63);
    private readonly TransactionProcessor _processor = new(new IntentDeduplicator());
    private readonly Entity _alice;
    private readonly Entity _bob;
    private uint _sequence;

    public BagTests()
    {
        var scheduler = new SimulationScheduler(_world.Clock, _world.Events);
        _world.CreateSystems().RegisterAll(scheduler);
        _alice = _world.SpawnPlayerAvatar(Alice, Start);
        _bob = _world.SpawnPlayerAvatar(Bob, Start + new Vector3(2, 0, 0));
    }

    private Inventory Bag => _world.Economy.Inventories.Of(Alice);

    public void Dispose() => _world.Dispose();

    [Fact]
    public void Moving_a_stack_fills_an_empty_slot_merges_the_same_item_and_swaps_a_different_one()
    {
        int berries = Give("sweet_berries", 25);
        int fur = Give("fox_fur", 2);
        Assert.Equal((berries, 25, fur, 2), (Bag.Stacks[0].Item, Bag.Stacks[0].Count, Bag.Stacks[1].Item, Bag.Stacks[1].Count));

        Assert.True(Run(new MoveItemTransaction(_world, _alice, 0, 5)).IsApplied);
        Assert.Equal((0, berries, 25), (Bag.Stacks[0].Count, Bag.Stacks[5].Item, Bag.Stacks[5].Count));

        Assert.True(Run(new MoveItemTransaction(_world, _alice, 1, 5)).IsApplied);
        Assert.Equal((fur, berries), (Bag.Stacks[5].Item, Bag.Stacks[1].Item));

        Give("sweet_berries", 10);
        Assert.True(Run(new MoveItemTransaction(_world, _alice, 0, 1)).IsApplied);
        Assert.Equal((30, 5), (Bag.Stacks[1].Count, Bag.Stacks[0].Count));

        Assert.Equal(IntentRejectReason.InvalidArguments, Run(new MoveItemTransaction(_world, _alice, 7, 2)).Reason);
        Assert.Equal(IntentRejectReason.InvalidArguments, Run(new MoveItemTransaction(_world, _alice, 0, 99)).Reason);
    }

    [Fact]
    public void A_companion_at_ones_side_carries_a_bag_sized_by_its_body()
    {
        Entity owl = Pacted("hollow_owl", Alice, summoned: true);
        Entity calf = Pacted("mossback", Alice, summoned: true);
        int feathers = Give("owl_feather", 30 * 5);

        for (int slot = 0; slot < 4; slot++)
        {
            Assert.True(Run(new TransferItemTransaction(_world, _alice, IdOf(owl), slot, ToPlayer: false)).IsApplied);
        }

        Assert.Equal(GearRefusal.InventoryFull, Refusal(new TransferItemTransaction(_world, _alice, IdOf(owl), 4, ToPlayer: false)));
        Assert.Equal(4 * 30, _world.Economy.CreatureBags.Find(IdOf(owl).Value)!.CountOf(feathers));
        Assert.Equal(8, Bags.OfCreature(_world, calf).Capacity);

        Assert.True(Run(new TransferItemTransaction(_world, _alice, IdOf(owl), 2, ToPlayer: true)).IsApplied);
        Assert.Equal(2 * 30, Bag.CountOf(feathers));
    }

    [Fact]
    public void Only_ones_own_companion_at_ones_side_takes_items()
    {
        Entity away = Pacted("hollow_owl", Alice, summoned: false);
        Entity bobs = Pacted("mossback", Bob, summoned: true);
        Entity wild = _world.SpawnCreature(_world.SpeciesIndex("mossback"), Start + new Vector3(0, 0, 4), new Personality(0, 0, 0, 0), group: 3);
        Give("fox_fur", 2);

        Assert.Equal(GearRefusal.NotWithYou, Refusal(new TransferItemTransaction(_world, _alice, IdOf(away), 0, ToPlayer: false)));
        Assert.Equal(IntentRejectReason.InvalidTarget, Run(new TransferItemTransaction(_world, _alice, IdOf(bobs), 0, ToPlayer: false)).Reason);
        Assert.Equal(IntentRejectReason.InvalidTarget, Run(new TransferItemTransaction(_world, _alice, IdOf(wild), 0, ToPlayer: false)).Reason);
        Assert.Equal(IntentRejectReason.InvalidTarget, Run(new TransferItemTransaction(_world, _alice, new StableId(0), 0, ToPlayer: false)).Reason);
    }

    [Fact]
    public void A_transfer_happens_once_however_often_it_is_sent()
    {
        Entity owl = Pacted("hollow_owl", Alice, summoned: true);
        int fur = Give("fox_fur", 3);

        var transfer = new TransferItemTransaction(_world, _alice, IdOf(owl), 0, ToPlayer: false);
        IntentResult first = _processor.Execute(new IntentId(3, 1), ref transfer, 0);
        IntentResult again = _processor.Execute(new IntentId(3, 1), ref transfer, 0);

        Assert.Equal(first, again);
        Assert.Equal((0, 3), (Bag.CountOf(fur), _world.Economy.CreatureBags.Find(IdOf(owl).Value)!.CountOf(fur)));
    }

    [Fact]
    public void Food_restores_health_and_stamina_up_to_full_and_is_used_up()
    {
        int meal = Give("berry_meal", 2);
        ref Combatant me = ref _world.World.GetComponent<Combatant>(_alice);
        me.Health = me.MaxHealth - 10;
        me.Stamina = 0;

        Assert.True(Run(new UseItemTransaction(_world, _alice, new StableId(0), 0)).IsApplied);

        Combatant after = _world.World.GetComponent<Combatant>(_alice);
        Assert.Equal((after.MaxHealth, 30), (after.Health, after.Stamina));
        Assert.Equal(1, Bag.CountOf(meal));
        Give("fox_fur", 1);
        Assert.Equal(GearRefusal.NotUsable, Refusal(new UseItemTransaction(_world, _alice, new StableId(0), 1)));
    }

    [Fact]
    public void Food_can_be_given_to_a_companion_at_ones_side_but_not_one_who_is_down()
    {
        Entity calf = Pacted("mossback", Alice, summoned: true);
        Give("berry_meal", 2);
        ref Combatant calfStats = ref _world.World.GetComponent<Combatant>(calf);
        calfStats.Health = 1;

        Assert.True(Run(new UseItemTransaction(_world, _alice, IdOf(calf), 0)).IsApplied);
        Assert.Equal(41, _world.World.GetComponent<Combatant>(calf).Health);

        _world.World.GetComponent<Combatant>(calf).Health = 0;
        Assert.Equal(GearRefusal.Down, Refusal(new UseItemTransaction(_world, _alice, IdOf(calf), 0)));
    }

    [Fact]
    public void A_creatures_bag_is_saved_slot_by_slot_with_the_creature()
    {
        Entity owl = Pacted("hollow_owl", Alice, summoned: true);
        Give("fox_fur", 2);
        Give("owl_feather", 1);
        Run(new TransferItemTransaction(_world, _alice, IdOf(owl), 1, ToPlayer: false));

        CreatureSave save = WorldSnapshots.CaptureCreature(_world, owl);

        Assert.Equal([new SavedItem("owl_feather", 1)], save.Bag);
        using CreatureWorld restored = WorldSnapshots.Restore(ShippedData.Current, new CreatureWorldSettings(), new WorldSnapshot(0, 1, save.Id.Value, [save], []));
        Assert.Equal(1, restored.Economy.CreatureBags.Find(save.Id.Value)!.CountOf(_world.Data.ItemIndex("owl_feather")));
        Assert.Equal(4, restored.Economy.CreatureBags.Find(save.Id.Value)!.Capacity);
    }

    [Fact]
    public void A_players_bag_keeps_its_gaps_when_saved()
    {
        Give("fox_fur", 2);
        Give("owl_feather", 1);
        Run(new MoveItemTransaction(_world, _alice, 1, 4));

        InventorySave saved = _world.Economy.Export(Alice, _world.Data);
        _world.Economy.Import(Bob, saved, _world.Data);

        Assert.Equal(5, saved.Items.Count);
        Assert.Equal(_world.Data.ItemIndex("owl_feather"), _world.Economy.Inventories.Of(Bob).Stacks[4].Item);
        Assert.Equal(0, _world.Economy.Inventories.Of(Bob).Stacks[2].Count);
    }

    [Fact]
    public void Body_types_carry_different_amounts()
    {
        Assert.Equal((4, 3_000), CreatureBags.SizeOf(BodyType.Avian));
        Assert.Equal((8, 15_000), CreatureBags.SizeOf(BodyType.Quadruped));
    }

    private int Give(string item, int count)
    {
        int index = _world.Data.ItemIndex(item);
        Bag.Add(_world.Data, [new ItemCount(index, count)]);
        return index;
    }

    private Entity Pacted(string species, ulong owner, bool summoned)
    {
        Entity creature = _world.SpawnCreature(_world.SpeciesIndex(species), Start + new Vector3(3, 0, 3), new Personality(0, 0, 0, 0), group: 7);
        _world.Services.Bonding.Restore(
            _world.World.GetComponent<MindRef>(creature).Handle, new BondState(OwnershipState.Pacted, owner, PactTerms.Of(0), 0, 1, summoned, 0, 0, false));
        return creature;
    }

    private StableId IdOf(Entity creature) => _world.World.GetComponent<StableId>(creature);

    private GearRefusal Refusal<T>(T transaction)
        where T : struct, ITransaction
    {
        IntentResult result = Run(transaction);
        Assert.Equal(IntentRejectReason.WrongState, result.Reason);
        return (GearRefusal)result.Detail;
    }

    private IntentResult Run<T>(T transaction)
        where T : struct, ITransaction
    {
        return _processor.Execute(new IntentId(2, ++_sequence), ref transaction, 0);
    }
}
