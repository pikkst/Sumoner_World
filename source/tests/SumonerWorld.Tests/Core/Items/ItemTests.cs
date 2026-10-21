using System.Numerics;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Items;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.Transactions;
using SumonerWorld.Core.World;
using SumonerWorld.Scenarios;

namespace SumonerWorld.Tests.Core.Items;

public sealed class ItemTests : IDisposable
{
    private const ulong Alice = 1001;
    private const ulong Bob = 1002;
    private static readonly Vector3 Start = new(100, 0, 100);

    private readonly CreatureWorld _world = new(ShippedData.Current, new CreatureWorldSettings(), seed: 61);
    private readonly SimulationScheduler _scheduler;
    private readonly TransactionProcessor _processor = new(new IntentDeduplicator());
    private readonly Entity _alice;
    private readonly Entity _bob;
    private uint _sequence;

    public ItemTests()
    {
        _scheduler = new SimulationScheduler(_world.Clock, _world.Events);
        _world.CreateSystems().RegisterAll(_scheduler);
        _alice = _world.SpawnPlayerAvatar(Alice, Start);
        _bob = _world.SpawnPlayerAvatar(Bob, Start + new Vector3(2, 0, 0));
    }

    public void Dispose() => _world.Dispose();

    [Fact]
    public void An_inventory_stacks_items_and_refuses_what_does_not_fit_in_slots_or_weight()
    {
        var inventory = new Inventory();
        int fur = _world.Data.ItemIndex("fox_fur");
        int branch = _world.Data.ItemIndex("fallen_branch");

        inventory.Add(_world.Data, [new ItemCount(fur, 25)]);
        Assert.Equal(25, inventory.CountOf(fur));
        Assert.Equal(2, inventory.Stacks.ToArray().Count(slot => slot.Count > 0));

        Assert.False(inventory.CanAdd(_world.Data, [new ItemCount(branch, 100)]), "40 kg of branches is too heavy");
        Assert.True(inventory.CanAdd(_world.Data, [new ItemCount(branch, 30)]));
        Assert.False(inventory.CanAdd(_world.Data, [new ItemCount(fur, 20 * Inventory.Slots)]), "more fur than slots");

        Assert.True(inventory.Remove(fur, 24));
        Assert.False(inventory.Remove(fur, 2));
        Assert.Equal(1, inventory.CountOf(fur));
        inventory.AddGold(10);
        Assert.False(inventory.TrySpend(11));
        Assert.True(inventory.TrySpend(4));
        Assert.Equal(6, inventory.Gold);
    }

    [Fact]
    public void A_defeated_creature_leaves_a_bag_for_each_player_who_fought_it_and_bags_vanish_after_five_minutes()
    {
        Entity fox = _world.SpawnCreature(_world.SpeciesIndex("bogfox"), Start + new Vector3(0, 0, 6), new Personality(0, 0, 0, 0), group: 3);
        _world.Events.Publish(new CombatantDefeated(fox, _world.SpeciesIndex("bogfox"), Alice, 0, Start + new Vector3(0, 0, 6), new Fighters(Alice, Bob, 0, 0)));
        _scheduler.RunTick();

        LootBag[] bags = [.. _world.Economy.Bags];
        Assert.Equal([Alice, Bob], bags.Select(bag => bag.Owner));
        Assert.All(bags, bag => Assert.InRange(bag.Gold, 3, 8));

        for (ulong i = 0; i < Economy.BagTicks; i++)
        {
            _scheduler.RunTick();
        }

        Assert.Empty(_world.Economy.Bags);
    }

    [Fact]
    public void Everyone_who_hits_a_creature_is_counted_among_its_fighters()
    {
        Entity fox = _world.SpawnCreature(_world.SpeciesIndex("bogfox"), Start + new Vector3(1, 0, 1.5f), new Personality(100, 0, 0, 0), group: 3);
        Assert.True(Attack(_alice, fox).IsApplied);
        Assert.True(Attack(_bob, fox).IsApplied);

        for (int i = 0; i < 40; i++)
        {
            _scheduler.RunTick();
        }

        Fighters fighters = _world.World.GetComponent<Fighters>(fox);
        Assert.Contains(Alice, new[] { fighters.A, fighters.B });
        Assert.Contains(Bob, new[] { fighters.A, fighters.B });
        Assert.Equal(fighters, fighters.With(Alice));
    }

    [Fact]
    public void Picking_up_a_bag_is_for_its_owner_in_reach_once_however_often_it_is_sent()
    {
        int fur = _world.Data.ItemIndex("fox_fur");
        LootBag bag = _world.Economy.Drop(Alice, Start + new Vector3(1, 0, 0), 7, [new ItemCount(fur, 2)], 0);
        LootBag far = _world.Economy.Drop(Alice, Start + new Vector3(10, 0, 0), 1, [], 0);

        Assert.Equal(IntentRejectReason.NotOwner, Run(new PickUpTransaction(_world, _bob, bag.Id)).Reason);
        Assert.Equal(IntentRejectReason.OutOfRange, Run(new PickUpTransaction(_world, _alice, far.Id)).Reason);

        var pickUp = new PickUpTransaction(_world, _alice, bag.Id);
        IntentResult first = _processor.Execute(new IntentId(1, 99), ref pickUp, 0);
        IntentResult again = _processor.Execute(new IntentId(1, 99), ref pickUp, 0);

        Assert.Equal(first, again);
        Assert.Equal(7, first.Detail);
        Assert.Equal((7, 2), (_world.Economy.Inventories.Of(Alice).Gold, _world.Economy.Inventories.Of(Alice).CountOf(fur)));
        Assert.Null(_world.Economy.BagWithId(bag.Id));
    }

    [Fact]
    public void A_full_inventory_leaves_the_bag_where_it_lies()
    {
        int branch = _world.Data.ItemIndex("fallen_branch");
        _world.Economy.Inventories.Of(Alice).Add(_world.Data, [new ItemCount(branch, 70)]);
        LootBag bag = _world.Economy.Drop(Alice, Start, 3, [new ItemCount(branch, 10)], 0);

        IntentResult result = Run(new PickUpTransaction(_world, _alice, bag.Id));

        Assert.Equal((IntentRejectReason.WrongState, ItemRefusal.InventoryFull), (result.Reason, (ItemRefusal)result.Detail));
        Assert.NotNull(_world.Economy.BagWithId(bag.Id));
        Assert.Equal(0, _world.Economy.Inventories.Of(Alice).Gold);
    }

    [Fact]
    public void Gathering_gives_the_nodes_item_once_per_intent_and_the_node_regrows_later()
    {
        int berryBush = _world.Data.GatherNodes.Select((node, i) => (node, i)).Single(item => item.node.Id == "berry_bush").i;
        GatherNode bush = _world.Economy.AddNode(berryBush, Start + new Vector3(1, 0, 1));
        int berries = _world.Data.ItemIndex("sweet_berries");

        var gather = new GatherTransaction(_world, _alice, bush.Id);
        IntentResult first = _processor.Execute(new IntentId(1, 50), ref gather, 10);
        IntentResult again = _processor.Execute(new IntentId(1, 50), ref gather, 11);

        Assert.Equal(first, again);
        Assert.InRange(first.Detail, 2, 4);
        Assert.Equal(first.Detail, _world.Economy.Inventories.Of(Alice).CountOf(berries));
        Assert.Equal(ItemRefusal.NotReady, (ItemRefusal)Run(new GatherTransaction(_world, _alice, bush.Id), now: 1_000).Detail);
        Assert.True(Run(new GatherTransaction(_world, _alice, bush.Id), now: 10 + (ulong)_world.Data.GatherNodes[berryBush].RegrowTicks).IsApplied);
    }

    [Fact]
    public void Nodes_are_placed_the_same_way_from_the_same_seed()
    {
        var a = new Economy();
        var b = new Economy();
        a.PlaceNodes(_world.Data, null, Start, 4, 7);
        b.PlaceNodes(_world.Data, null, Start, 4, 7);

        Assert.Equal(4 * _world.Data.GatherNodes.Count, a.Nodes.Count);
        Assert.Equal(a.Nodes.Select(node => (node.Kind, node.Position)), b.Nodes.Select(node => (node.Kind, node.Position)));
        Assert.All(a.Nodes, node => Assert.InRange(Vector3.Distance(node.Position, Start), 7.9f, 30.1f));
    }

    [Fact]
    public void An_inventory_is_saved_by_item_id_and_unknown_items_are_dropped_on_load()
    {
        int fur = _world.Data.ItemIndex("fox_fur");
        _world.Economy.Inventories.Of(Alice).Add(_world.Data, [new ItemCount(fur, 3)]);
        _world.Economy.Inventories.Of(Alice).AddGold(42);

        InventorySave saved = _world.Economy.Export(Alice, _world.Data);
        Assert.Equal(new InventorySave(42, [new SavedItem("fox_fur", 3)]), saved);

        _world.Economy.Import(Bob, saved with { Items = [.. saved.Items, new SavedItem("dragon_scale", 1)] }, _world.Data);
        Assert.Equal(saved, _world.Economy.Export(Bob, _world.Data));
    }

    private IntentResult Attack(Entity avatar, Entity target)
    {
        Vector3 aim = _world.World.GetComponent<Position>(target).Value - _world.World.GetComponent<Position>(avatar).Value;
        return Run(new AttackTransaction(_world, avatar, 0, SumonerWorld.Net.Messages.Snapshot.EncodeFacing(aim)));
    }

    private IntentResult Run<T>(T transaction, ulong now = 0)
        where T : struct, ITransaction
    {
        return _processor.Execute(new IntentId(2, ++_sequence), ref transaction, now);
    }
}
