using SumonerWorld.Core.Data;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Items;
using SumonerWorld.Net.Messages;

namespace SumonerWorld.Net.Client;

/// <summary>One thing a vendor offers: its shop entry, the item and its price.</summary>
/// <param name="Entry">Index in the vendor's shop.</param>
/// <param name="Item">Item index.</param>
/// <param name="Name">Item name.</param>
/// <param name="Price">Gold each.</param>
public readonly record struct ShopOffer(int Entry, int Item, string Name, int Price);

/// <summary>One stack in the player's bag the person would buy, at the item's value.</summary>
/// <param name="Slot">Bag slot.</param>
/// <param name="Item">Item index.</param>
/// <param name="Name">Item name.</param>
/// <param name="Count">How many lie in the slot.</param>
/// <param name="Value">Gold each.</param>
public readonly record struct SellOffer(int Slot, int Item, string Name, int Count, int Value);

/// <summary>What a shop window shows (E5b): the vendor's stock, and what in the player's bag they buy.</summary>
public static class ShopList
{
    /// <summary>The vendor's stock with prices.</summary>
    public static IReadOnlyList<ShopOffer> Offers(IDataRegistry data, NpcDefinition vendor)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(vendor);
        var offers = new List<ShopOffer>();
        for (int i = 0; i < vendor.Shop.Count; i++)
        {
            int item = data.ItemIndex(vendor.Shop[i].Item);
            offers.Add(new ShopOffer(i, item, InteractionText.SkillName(vendor.Shop[i].Item), vendor.Shop[i].Price));
        }

        return offers;
    }

    /// <summary>The stacks in <paramref name="bag"/> that <paramref name="buyer"/> buys, in slot order.</summary>
    public static IReadOnlyList<SellOffer> Sellable(IDataRegistry data, NpcDefinition buyer, InventoryList bag)
    {
        ArgumentNullException.ThrowIfNull(data);
        var offers = new List<SellOffer>();
        foreach (InventoryEntry entry in bag.Entries.OrderBy(entry => entry.Slot))
        {
            ItemDefinition item = data.Items[entry.Item];
            if (entry.Count > 0 && TradeRules.Buys(buyer, item))
            {
                offers.Add(new SellOffer(entry.Slot, entry.Item, InteractionText.SkillName(item.Id), entry.Count, item.GoldValue));
            }
        }

        return offers;
    }

    /// <summary>The intent target of a trade: the person in bits 0–7, the count in bits 8–15.</summary>
    public static ulong Target(int npc, int count) => (byte)npc | ((ulong)(byte)Math.Clamp(count, 1, TradeRules.MaxCount) << 8);
}
