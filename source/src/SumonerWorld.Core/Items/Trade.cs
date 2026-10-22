using System.Numerics;
using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Transactions;
using SumonerWorld.Core.World;

namespace SumonerWorld.Core.Items;

/// <summary>Why a trade or a rest was refused, in <see cref="IntentResult.Detail"/> (E5b).</summary>
public enum TradeRefusal : byte
{
    /// <summary>Not refused.</summary>
    None = 0,

    /// <summary>The player does not have the gold.</summary>
    NotEnoughGold = 1,

    /// <summary>The bag has no room or would be too heavy.</summary>
    BagFull = 2,

    /// <summary>This person does not buy that kind of thing.</summary>
    DoesNotBuy = 3,

    /// <summary>This person does not sell that, or keeps no rooms.</summary>
    NotForSale = 4,
}

/// <summary>Trading with the Vale's people (E5b): who is in reach and what they buy.</summary>
public static class TradeRules
{
    /// <summary>Most of one item bought or sold at once.</summary>
    public const int MaxCount = 20;

    /// <summary>A refusal with its reason.</summary>
    public static IntentResult Refuse(TradeRefusal reason) => new(IntentStatus.Rejected, IntentRejectReason.WrongState, (byte)reason);

    /// <summary>The person to trade with, if the index is valid and they stand within talking range of the player now.</summary>
    public static IntentResult? Check(CreatureWorld world, Entity avatar, int npc, ulong now)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!world.World.Store<PlayerAvatar>().Has(avatar))
        {
            return IntentResult.Reject(IntentRejectReason.InvalidTarget);
        }

        if ((uint)npc >= (uint)world.Data.People.Count)
        {
            return IntentResult.Reject(IntentRejectReason.InvalidArguments);
        }

        Vector3 at = world.World.GetComponent<Position>(avatar).Value;
        (float x, float z, float _) = People.At(world.Data.People[npc], now);
        return new Vector2(at.X - x, at.Z - z).Length() > Academy.TalkTransaction.Range ? IntentResult.Reject(IntentRejectReason.OutOfRange) : null;
    }

    /// <summary>True when <paramref name="person"/> buys <paramref name="item"/>: one of its tags or its category is on their list.</summary>
    public static bool Buys(NpcDefinition person, ItemDefinition item)
    {
        ArgumentNullException.ThrowIfNull(person);
        ArgumentNullException.ThrowIfNull(item);
        string category = item.Category.ToString().ToLowerInvariant();
        return item.GoldValue > 0 && person.Buys.Any(kind => kind == category || item.Tags.Contains(kind));
    }
}

/// <summary>A player buys <paramref name="Count"/> of a vendor's stock entry <paramref name="Entry"/> for its price each (E5b).</summary>
/// <param name="Creatures">The world.</param>
/// <param name="Avatar">The buyer.</param>
/// <param name="Npc">The vendor's index in <c>IDataRegistry.People</c>.</param>
/// <param name="Entry">The index in the vendor's shop.</param>
/// <param name="Count">How many.</param>
public record struct BuyTransaction(CreatureWorld Creatures, Entity Avatar, int Npc, int Entry, int Count) : ITransaction
{
    /// <inheritdoc />
    public readonly IntentResult Validate(ulong now)
    {
        if (TradeRules.Check(Creatures, Avatar, Npc, now) is IntentResult refused)
        {
            return refused;
        }

        NpcDefinition vendor = Creatures.Data.People[Npc];
        if ((uint)Entry >= (uint)vendor.Shop.Count || Count is < 1 or > TradeRules.MaxCount)
        {
            return TradeRules.Refuse(TradeRefusal.NotForSale);
        }

        ShopEntryDefinition entry = vendor.Shop[Entry];
        Inventory bag = Bags.OfPlayer(Creatures, Avatar);
        if (bag.Gold < entry.Price * Count)
        {
            return TradeRules.Refuse(TradeRefusal.NotEnoughGold);
        }

        return bag.CanAdd(Creatures.Data, [new ItemCount(Creatures.Data.ItemIndex(entry.Item), Count)]) ? IntentResult.Applied() : TradeRules.Refuse(TradeRefusal.BagFull);
    }

    /// <inheritdoc />
    public readonly IntentResult Commit(ulong now)
    {
        ShopEntryDefinition entry = Creatures.Data.People[Npc].Shop[Entry];
        Inventory bag = Bags.OfPlayer(Creatures, Avatar);
        bag.TrySpend(entry.Price * Count);
        bag.Add(Creatures.Data, [new ItemCount(Creatures.Data.ItemIndex(entry.Item), Count)]);
        return IntentResult.Applied();
    }
}

/// <summary>A player sells <paramref name="Count"/> of what lies in bag slot <paramref name="Slot"/> to a person who buys it, for its value each (E5b).</summary>
/// <param name="Creatures">The world.</param>
/// <param name="Avatar">The seller.</param>
/// <param name="Npc">The buyer's index in <c>IDataRegistry.People</c>.</param>
/// <param name="Slot">The bag slot.</param>
/// <param name="Count">How many.</param>
public record struct SellTransaction(CreatureWorld Creatures, Entity Avatar, int Npc, int Slot, int Count) : ITransaction
{
    /// <inheritdoc />
    public readonly IntentResult Validate(ulong now)
    {
        if (TradeRules.Check(Creatures, Avatar, Npc, now) is IntentResult refused)
        {
            return refused;
        }

        Inventory bag = Bags.OfPlayer(Creatures, Avatar);
        if ((uint)Slot >= (uint)bag.Capacity || bag.Stacks[Slot].Count < Count || Count is < 1 or > TradeRules.MaxCount)
        {
            return IntentResult.Reject(IntentRejectReason.InvalidArguments);
        }

        ItemDefinition item = Creatures.Data.Items[bag.Stacks[Slot].Item];
        return TradeRules.Buys(Creatures.Data.People[Npc], item) ? IntentResult.Applied() : TradeRules.Refuse(TradeRefusal.DoesNotBuy);
    }

    /// <inheritdoc />
    public readonly IntentResult Commit(ulong now)
    {
        Inventory bag = Bags.OfPlayer(Creatures, Avatar);
        int item = bag.Stacks[Slot].Item;
        for (int i = 0; i < Count; i++)
        {
            bag.TakeOne(Slot);
        }

        bag.AddGold(Creatures.Data.Items[item].GoldValue * Count);
        return IntentResult.Applied();
    }
}

/// <summary>A player rests at an inn (E5b): for the innkeeper's price, health, stamina and mana come back in full.</summary>
/// <param name="Creatures">The world.</param>
/// <param name="Avatar">The guest.</param>
/// <param name="Npc">The innkeeper's index in <c>IDataRegistry.People</c>.</param>
public record struct RestTransaction(CreatureWorld Creatures, Entity Avatar, int Npc) : ITransaction
{
    /// <inheritdoc />
    public readonly IntentResult Validate(ulong now)
    {
        if (TradeRules.Check(Creatures, Avatar, Npc, now) is IntentResult refused)
        {
            return refused;
        }

        int price = Creatures.Data.People[Npc].RestPrice;
        if (price <= 0)
        {
            return TradeRules.Refuse(TradeRefusal.NotForSale);
        }

        return Bags.OfPlayer(Creatures, Avatar).Gold < price ? TradeRules.Refuse(TradeRefusal.NotEnoughGold) : IntentResult.Applied();
    }

    /// <inheritdoc />
    public readonly IntentResult Commit(ulong now)
    {
        Bags.OfPlayer(Creatures, Avatar).TrySpend(Creatures.Data.People[Npc].RestPrice);
        ref Combatant guest = ref Creatures.World.GetComponent<Combatant>(Avatar);
        guest.Health = guest.MaxHealth;
        guest.Stamina = guest.MaxStamina;
        guest.Mana = guest.MaxMana;
        return IntentResult.Applied();
    }
}
