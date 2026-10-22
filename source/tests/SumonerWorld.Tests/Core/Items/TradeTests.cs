using System.Numerics;
using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Items;
using SumonerWorld.Core.Transactions;
using SumonerWorld.Core.World;
using SumonerWorld.Net.Client;
using SumonerWorld.Net.Messages;
using SumonerWorld.Scenarios;

namespace SumonerWorld.Tests.Core.Items;

/// <summary>Trading with the Vale's people and resting at the inn (E5b).</summary>
public sealed class TradeTests : IDisposable
{
    private const ulong Alice = 1001;

    private readonly CreatureWorld _world = new(ShippedData.Current, new CreatureWorldSettings(), seed: 72);
    private readonly TransactionProcessor _processor = new(new IntentDeduplicator());
    private uint _sequence;

    public void Dispose() => _world.Dispose();

    [Fact]
    public void Buying_costs_the_price_and_fills_the_bag_and_needs_the_gold()
    {
        (int pell, Entity alice) = Beside("pell");
        Inventory bag = Bags.OfPlayer(_world, alice);
        bag.AddGold(25);
        int meal = _world.Data.ItemIndex("berry_meal");

        Assert.True(Run(new BuyTransaction(_world, alice, pell, 0, 2)).IsApplied);
        Assert.Equal((5, 2), (bag.Gold, bag.CountOf(meal)));

        IntentResult broke = Run(new BuyTransaction(_world, alice, pell, 0, 1));
        Assert.Equal((IntentRejectReason.WrongState, (byte)TradeRefusal.NotEnoughGold), (broke.Reason, broke.Detail));
        Assert.Equal(IntentRejectReason.WrongState, Run(new BuyTransaction(_world, alice, pell, 9, 1)).Reason);
    }

    [Fact]
    public void Selling_pays_the_value_only_to_someone_who_buys_that_kind_of_thing()
    {
        (int juniper, Entity alice) = Beside("juniper");
        Inventory bag = Bags.OfPlayer(_world, alice);
        bag.Add(_world.Data, [new ItemCount(_world.Data.ItemIndex("sweet_berries"), 4), new ItemCount(_world.Data.ItemIndex("raw_ore"), 1)]);
        int berries = Slot(bag, "sweet_berries");
        int ore = Slot(bag, "raw_ore");

        Assert.True(Run(new SellTransaction(_world, alice, juniper, berries, 3)).IsApplied);
        Assert.Equal((3, 1), (bag.Gold, bag.CountOf(_world.Data.ItemIndex("sweet_berries"))));
        Assert.Equal((byte)TradeRefusal.DoesNotBuy, Run(new SellTransaction(_world, alice, juniper, ore, 1)).Detail);
        Assert.Equal(IntentRejectReason.InvalidArguments, Run(new SellTransaction(_world, alice, juniper, berries, 5)).Reason);
    }

    [Fact]
    public void Nobody_sells_for_less_than_they_buy_back_so_trade_cannot_make_gold()
    {
        foreach (NpcDefinition vendor in _world.Data.People)
        {
            foreach (ShopEntryDefinition entry in vendor.Shop)
            {
                Assert.True(entry.Price >= _world.Data.Items[_world.Data.ItemIndex(entry.Item)].GoldValue, $"{vendor.Id} sells {entry.Item}");
            }
        }
    }

    [Fact]
    public void A_rest_at_the_inn_costs_its_price_and_fills_health_stamina_and_mana()
    {
        (int marta, Entity alice) = Beside("marta");
        Bags.OfPlayer(_world, alice).AddGold(5);
        ref Combatant guest = ref _world.World.GetComponent<Combatant>(alice);
        guest = guest with { Health = 10, Stamina = 5, Mana = 1 };

        Assert.True(Run(new RestTransaction(_world, alice, marta)).IsApplied);

        Combatant rested = _world.World.GetComponent<Combatant>(alice);
        Assert.Equal((rested.MaxHealth, rested.MaxStamina, rested.MaxMana, 2), (rested.Health, rested.Stamina, rested.Mana, Bags.OfPlayer(_world, alice).Gold));
        (int pell, Entity bob) = Beside("pell", 1002);
        Bags.OfPlayer(_world, bob).AddGold(5);
        Assert.Equal((byte)TradeRefusal.NotForSale, Run(new RestTransaction(_world, bob, pell)).Detail);
    }

    [Fact]
    public void Trading_with_someone_out_of_reach_is_refused()
    {
        int pell = People.IndexOf(_world.Data, "pell");
        Entity far = _world.SpawnPlayerAvatar(1003, new Vector3(100, 0, 100));
        Bags.OfPlayer(_world, far).AddGold(100);

        Assert.Equal(IntentRejectReason.OutOfRange, Run(new BuyTransaction(_world, far, pell, 0, 1)).Reason);
    }

    [Fact]
    public void The_shop_window_lists_stock_and_what_they_buy_and_packs_the_target()
    {
        NpcDefinition hadda = _world.Data.People[People.IndexOf(_world.Data, "hadda")];
        int ore = _world.Data.ItemIndex("raw_ore");
        int berries = _world.Data.ItemIndex("sweet_berries");
        var bag = new InventoryList(10, [new InventoryEntry(berries, 3, 0), new InventoryEntry(ore, 2, 4)]);

        Assert.Equal("Iron staff", ShopList.Offers(_world.Data, hadda)[2].Name);
        Assert.Equal([new SellOffer(4, ore, "Raw ore", 2, 3)], ShopList.Sellable(_world.Data, hadda, bag));
        Assert.Equal(0x0503UL, ShopList.Target(3, 5));
        Assert.Equal(["Trade"], AcademyText.PersonPage(hadda, 1).Options);
        Assert.Equal(["Trade", "Rest (3 gold)"], AcademyText.PersonPage(_world.Data.People[People.IndexOf(_world.Data, "marta")], 1).Options);
    }

    private (int Npc, Entity Avatar) Beside(string id, ulong account = Alice)
    {
        int npc = People.IndexOf(_world.Data, id);
        NpcDefinition person = _world.Data.People[npc];
        (float x, float z, float _) = People.At(person, _world.Clock.Current.Index);
        return (npc, _world.SpawnPlayerAvatar(account, new Vector3(x + 1.5f, 0, z)));
    }

    private int Slot(Inventory bag, string item)
    {
        int index = _world.Data.ItemIndex(item);
        for (int i = 0; i < bag.Capacity; i++)
        {
            if (bag.Stacks[i].Item == index && bag.Stacks[i].Count > 0)
            {
                return i;
            }
        }

        return -1;
    }

    private IntentResult Run<T>(T transaction)
        where T : struct, ITransaction
    {
        return _processor.Execute(new IntentId(1, ++_sequence), ref transaction, _world.Clock.Current.Index);
    }
}
