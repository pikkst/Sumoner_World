using SumonerWorld.Core.Data;
using SumonerWorld.Core.Diagnostics;

namespace SumonerWorld.Core.Items;

/// <summary>A number of one item (index into the items data): an inventory slot or a share of loot.</summary>
/// <param name="Item">Item index.</param>
/// <param name="Count">How many (0 = an empty slot).</param>
public readonly record struct ItemCount(int Item, int Count);

/// <summary>
/// A bag (crafting spec §2–3, M2-13): a player's (<see cref="Slots"/> slots of stacks, a weight limit and gold) or a creature's
/// (fewer slots by body type, M2-14b). Server state: it changes only inside transactions (R27, R31).
/// </summary>
/// <param name="capacity">Slots.</param>
/// <param name="maxWeightGrams">The most it carries, in grams.</param>
public sealed class Inventory(int capacity = Inventory.Slots, int maxWeightGrams = Inventory.MaxWeightGrams)
{
    /// <summary>Slots a player carries.</summary>
    public const int Slots = 24;

    /// <summary>The most a player carries, in grams.</summary>
    public const int MaxWeightGrams = 30_000;

    private readonly ItemCount[] _slots = new ItemCount[capacity];

    /// <summary>Slots in this bag.</summary>
    public int Capacity => _slots.Length;

    /// <summary>The most this bag carries, in grams.</summary>
    public int MaxGrams => maxWeightGrams;

    /// <summary>Gold (never below 0).</summary>
    public int Gold { get; private set; }

    /// <summary>The slots (empty ones have count 0).</summary>
    public ReadOnlySpan<ItemCount> Stacks => _slots;

    /// <summary>What everything carried weighs, in grams.</summary>
    public int WeightGrams(IDataRegistry data)
    {
        ArgumentNullException.ThrowIfNull(data);
        int grams = 0;
        foreach (ItemCount stack in _slots)
        {
            grams += stack.Count == 0 ? 0 : data.Items[stack.Item].WeightGrams * stack.Count;
        }

        return grams;
    }

    /// <summary>How many of an item it holds.</summary>
    public int CountOf(int item)
    {
        int count = 0;
        foreach (ItemCount stack in _slots)
        {
            count += stack.Item == item ? stack.Count : 0;
        }

        return count;
    }

    /// <summary>True when all of <paramref name="items"/> fit (slots and weight); nothing changes.</summary>
    public bool CanAdd(IDataRegistry data, IReadOnlyList<ItemCount> items)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(items);
        int grams = WeightGrams(data);
        var trial = (ItemCount[])_slots.Clone();
        foreach (ItemCount stack in items)
        {
            grams += data.Items[stack.Item].WeightGrams * stack.Count;
            if (!Place(trial, data, stack))
            {
                return false;
            }
        }

        return grams <= maxWeightGrams;
    }

    /// <summary>Adds items that <see cref="CanAdd"/> allowed.</summary>
    public void Add(IDataRegistry data, IReadOnlyList<ItemCount> items)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(items);
        foreach (ItemCount stack in items)
        {
            if (!Place(_slots, data, stack))
            {
                throw new InvalidOperationException("Add without CanAdd: the items do not fit.");
            }
        }
    }

    /// <summary>Removes <paramref name="count"/> of an item; false (and nothing removed) when it holds fewer.</summary>
    public bool Remove(int item, int count)
    {
        if (CountOf(item) < count)
        {
            return false;
        }

        for (int i = _slots.Length - 1; i >= 0 && count > 0; i--)
        {
            if (_slots[i].Item == item && _slots[i].Count > 0)
            {
                int taken = Math.Min(count, _slots[i].Count);
                _slots[i] = _slots[i].Count == taken ? default : _slots[i] with { Count = _slots[i].Count - taken };
                count -= taken;
            }
        }

        return true;
    }

    /// <summary>Takes one slot's whole stack out (empty when the slot is empty or out of range).</summary>
    public ItemCount TakeSlot(int slot)
    {
        if ((uint)slot >= (uint)_slots.Length)
        {
            return default;
        }

        ItemCount stack = _slots[slot];
        _slots[slot] = default;
        return stack;
    }

    /// <summary>Removes one item from a slot; false when the slot is empty.</summary>
    public bool TakeOne(int slot)
    {
        if ((uint)slot >= (uint)_slots.Length || _slots[slot].Count == 0)
        {
            return false;
        }

        _slots[slot] = _slots[slot].Count == 1 ? default : _slots[slot] with { Count = _slots[slot].Count - 1 };
        return true;
    }

    /// <summary>
    /// Moves a slot's stack onto another slot: into an empty one, onto the same item (as many as fit there, the rest stays), or
    /// swapping places with a different item. False (nothing moved) when either slot is out of range or the source is empty.
    /// </summary>
    public bool Move(IDataRegistry data, int from, int to)
    {
        ArgumentNullException.ThrowIfNull(data);
        if ((uint)from >= (uint)_slots.Length || (uint)to >= (uint)_slots.Length || from == to || _slots[from].Count == 0)
        {
            return false;
        }

        ItemCount source = _slots[from];
        ItemCount target = _slots[to];
        if (target.Count > 0 && target.Item == source.Item)
        {
            int moved = Math.Min(source.Count, data.Items[source.Item].Stack - target.Count);
            _slots[to] = target with { Count = target.Count + moved };
            _slots[from] = source.Count == moved ? default : source with { Count = source.Count - moved };
            return true;
        }

        _slots[to] = source;
        _slots[from] = target;
        return true;
    }

    /// <summary>Adds gold.</summary>
    public void AddGold(int gold)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(gold);
        Gold += gold;
    }

    /// <summary>Spends gold; false (and nothing spent) when there is not enough.</summary>
    public bool TrySpend(int gold)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(gold);
        if (Gold < gold)
        {
            return false;
        }

        Gold -= gold;
        return true;
    }

    /// <summary>Sets gold and slots from a save.</summary>
    public void Restore(int gold, IReadOnlyList<ItemCount> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        Gold = Math.Max(0, gold);
        Array.Clear(_slots);
        for (int i = 0; i < Math.Min(_slots.Length, slots.Count); i++)
        {
            _slots[i] = slots[i];
        }
    }

    /// <summary>Adds the inventory to a state hash.</summary>
    public void AddTo(ref StateHash hash)
    {
        hash.Add((ulong)Gold);
        foreach (ItemCount stack in _slots)
        {
            hash.Add((ulong)stack.Item);
            hash.Add((ulong)stack.Count);
        }
    }

    // Tops up stacks of the same item first, then fills empty slots.
    private static bool Place(ItemCount[] slots, IDataRegistry data, ItemCount stack)
    {
        int left = stack.Count;
        int max = data.Items[stack.Item].Stack;
        for (int i = 0; i < slots.Length && left > 0; i++)
        {
            if (slots[i].Count > 0 && slots[i].Item == stack.Item && slots[i].Count < max)
            {
                int added = Math.Min(left, max - slots[i].Count);
                slots[i] = slots[i] with { Count = slots[i].Count + added };
                left -= added;
            }
        }

        for (int i = 0; i < slots.Length && left > 0; i++)
        {
            if (slots[i].Count == 0)
            {
                int added = Math.Min(left, max);
                slots[i] = new ItemCount(stack.Item, added);
                left -= added;
            }
        }

        return left == 0;
    }
}

/// <summary>Every player's inventory, by account (created empty on first use).</summary>
public sealed class InventoryStore
{
    private readonly SortedDictionary<ulong, Inventory> _byAccount = [];

    /// <summary>Accounts with an inventory.</summary>
    public IEnumerable<ulong> Accounts => _byAccount.Keys;

    /// <summary>The account's inventory.</summary>
    public Inventory Of(ulong account)
    {
        if (!_byAccount.TryGetValue(account, out Inventory? inventory))
        {
            inventory = new Inventory();
            _byAccount.Add(account, inventory);
        }

        return inventory;
    }

    /// <summary>True when the account has an inventory.</summary>
    public bool Has(ulong account) => _byAccount.ContainsKey(account);

    /// <summary>Adds every inventory, in account order, to a state hash.</summary>
    public void AddTo(ref StateHash hash)
    {
        foreach ((ulong account, Inventory inventory) in _byAccount)
        {
            hash.Add(account);
            inventory.AddTo(ref hash);
        }
    }

    /// <summary>A short line for logs: the account's gold and what it carries.</summary>
    public string Describe(ulong account, IDataRegistry data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Inventory inventory = Of(account);
        IEnumerable<string> items = inventory.Stacks.ToArray().Where(stack => stack.Count > 0).Select(stack => $"{data.Items[stack.Item].Id} x{stack.Count}");
        return $"{inventory.Gold} gold; {string.Join(", ", items)}";
    }
}

/// <summary>
/// Pacted creatures' own bags, by stable id (M2-14b): the owner moves items in and out while it is at their side. Its size
/// follows its body (<see cref="SizeOf"/>).
/// </summary>
public sealed class CreatureBags
{
    private readonly SortedDictionary<ulong, Inventory> _byCreature = [];

    /// <summary>Creatures with a bag.</summary>
    public IEnumerable<ulong> Creatures => _byCreature.Keys;

    /// <summary>Slots and grams a body carries: birds little, four-legged creatures the most.</summary>
    public static (int Slots, int Grams) SizeOf(Data.Definitions.BodyType body) => body switch
    {
        Data.Definitions.BodyType.Avian => (4, 3_000),
        Data.Definitions.BodyType.Quadruped => (8, 15_000),
        Data.Definitions.BodyType.Biped => (6, 10_000),
        _ => (6, 8_000),
    };

    /// <summary>The creature's bag (created empty on first use).</summary>
    public Inventory Of(ulong creature, Data.Definitions.BodyType body)
    {
        if (!_byCreature.TryGetValue(creature, out Inventory? bag))
        {
            (int slots, int grams) = SizeOf(body);
            bag = new Inventory(slots, grams);
            _byCreature.Add(creature, bag);
        }

        return bag;
    }

    /// <summary>The creature's bag if it has one.</summary>
    public Inventory? Find(ulong creature) => _byCreature.GetValueOrDefault(creature);

    /// <summary>Adds every bag, in creature order, to a state hash.</summary>
    public void AddTo(ref StateHash hash)
    {
        foreach ((ulong creature, Inventory bag) in _byCreature)
        {
            hash.Add(creature);
            bag.AddTo(ref hash);
        }
    }
}
