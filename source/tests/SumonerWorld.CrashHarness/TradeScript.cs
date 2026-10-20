using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Randomness;
using SumonerWorld.Persistence;
using SumonerWorld.Persistence.Records;

namespace SumonerWorld.CrashHarness;

/// <summary>What a trade needs to read and change: the world store inside a transaction, or an in-memory copy for the replay check.</summary>
public interface ITradeLedger
{
    /// <summary>Ids of an owner's items, ascending.</summary>
    IReadOnlyList<ulong> ItemsOwnedBy(ulong owner);

    /// <summary>Reads an item.</summary>
    bool TryGetItem(ulong id, out ulong owner, out ItemRecord item);

    /// <summary>Inserts or replaces an item.</summary>
    void PutItem(ulong id, ulong owner, ItemRecord item);

    /// <summary>Removes an item.</summary>
    void DeleteItem(ulong id);

    /// <summary>Reads a counter.</summary>
    long GetMeta(string key);

    /// <summary>Sets a counter.</summary>
    void SetMeta(string key, long value);
}

/// <summary>The store inside one transaction.</summary>
public sealed class TransactionLedger(IWorldTransaction transaction) : ITradeLedger
{
    /// <inheritdoc />
    public IReadOnlyList<ulong> ItemsOwnedBy(ulong owner) => transaction.ItemsOwnedBy(owner);

    /// <inheritdoc />
    public bool TryGetItem(ulong id, out ulong owner, out ItemRecord item) => transaction.TryGetItem(id, out owner, out item);

    /// <inheritdoc />
    public void PutItem(ulong id, ulong owner, ItemRecord item) => transaction.PutItem(id, owner, item);

    /// <inheritdoc />
    public void DeleteItem(ulong id) => transaction.DeleteItem(id);

    /// <inheritdoc />
    public long GetMeta(string key) => transaction.GetMeta(key);

    /// <inheritdoc />
    public void SetMeta(string key, long value) => transaction.SetMeta(key, value);
}

/// <summary>An in-memory ledger: the expected state, from applying every trade exactly once.</summary>
public sealed class MemoryLedger : ITradeLedger
{
    private readonly SortedDictionary<ulong, (ulong Owner, ItemRecord Item)> _items = [];
    private readonly Dictionary<string, long> _meta = [];

    /// <summary>All items, by id.</summary>
    public IReadOnlyList<(ulong Id, ulong Owner, ItemRecord Item)> Items => [.. _items.Select(entry => (entry.Key, entry.Value.Owner, entry.Value.Item))];

    /// <inheritdoc />
    public IReadOnlyList<ulong> ItemsOwnedBy(ulong owner) => [.. _items.Where(entry => entry.Value.Owner == owner).Select(entry => entry.Key)];

    /// <inheritdoc />
    public bool TryGetItem(ulong id, out ulong owner, out ItemRecord item)
    {
        bool found = _items.TryGetValue(id, out (ulong Owner, ItemRecord Item) entry);
        (owner, item) = found ? entry : (0, null!);
        return found;
    }

    /// <inheritdoc />
    public void PutItem(ulong id, ulong owner, ItemRecord item) => _items[id] = (owner, item);

    /// <inheritdoc />
    public void DeleteItem(ulong id) => _items.Remove(id);

    /// <inheritdoc />
    public long GetMeta(string key) => _meta.GetValueOrDefault(key);

    /// <inheritdoc />
    public void SetMeta(string key, long value) => _meta[key] = value;
}

/// <summary>
/// The spike-3 trade load (TDD §8): 8 players and 200 items. Trade <c>k</c> is drawn from <c>(seed, k)</c> and the current state:
/// 70 % swap 1–3 items of one player for 1–3 of another, 30 % craft (one item is consumed, a new one with a fresh id is made). Item
/// count stays 200, so any lost or duplicated item, or any trade applied twice, shows up against a replay.
/// </summary>
public static class TradeScript
{
    /// <summary>Players.</summary>
    public const int Players = 8;

    /// <summary>Items at the start.</summary>
    public const int Items = 200;

    /// <summary>Meta key of the next new item id.</summary>
    public const string NextItemKey = "next_item_id";

    private const int ItemKinds = 5;

    /// <summary>Creates the starting items.</summary>
    public static void Seed(ITradeLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        for (ulong id = 1; id <= Items; id++)
        {
            ledger.PutItem(id, (id % Players) + 1, new ItemRecord { Kind = (ushort)(id % ItemKinds) });
        }

        ledger.SetMeta(NextItemKey, Items + 1);
    }

    /// <summary>Applies trade <paramref name="trade"/>; returns its result.</summary>
    public static IntentResult Apply(ITradeLedger ledger, ulong seed, uint trade)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        var random = new SeededRandom((seed * 0x9E3779B97F4A7C15UL) + trade);
        ulong a = (ulong)random.NextInt(1, Players + 1);
        ulong b = ((a - 1 + (ulong)random.NextInt(1, Players)) % Players) + 1;
        if (random.NextInt(0, 10) < 3)
        {
            return Craft(ledger, random, a);
        }

        List<ulong> gives = Pick(ledger.ItemsOwnedBy(a), random);
        List<ulong> gets = Pick(ledger.ItemsOwnedBy(b), random);
        if (gives.Count == 0 || gets.Count == 0)
        {
            return IntentResult.Reject(IntentRejectReason.InvalidArguments);
        }

        Move(ledger, gives, b);
        Move(ledger, gets, a);
        return IntentResult.Applied(1);
    }

    private static IntentResult Craft(ITradeLedger ledger, SeededRandom random, ulong crafter)
    {
        IReadOnlyList<ulong> owned = ledger.ItemsOwnedBy(crafter);
        if (owned.Count == 0)
        {
            return IntentResult.Reject(IntentRejectReason.InvalidTarget);
        }

        ulong consumed = owned[random.NextInt(0, owned.Count)];
        ledger.TryGetItem(consumed, out _, out ItemRecord item);
        long next = ledger.GetMeta(NextItemKey);
        ledger.DeleteItem(consumed);
        ledger.PutItem((ulong)next, crafter, item with { Kind = (ushort)((item.Kind + 1) % ItemKinds) });
        ledger.SetMeta(NextItemKey, next + 1);
        return IntentResult.Applied(2);
    }

    private static List<ulong> Pick(IReadOnlyList<ulong> owned, SeededRandom random)
    {
        var pool = owned.ToList();
        int count = Math.Min(pool.Count, random.NextInt(1, 4));
        var picked = new List<ulong>(count);
        for (int i = 0; i < count; i++)
        {
            int index = random.NextInt(i, pool.Count);
            (pool[i], pool[index]) = (pool[index], pool[i]);
            picked.Add(pool[i]);
        }

        return picked;
    }

    private static void Move(ITradeLedger ledger, IReadOnlyList<ulong> items, ulong newOwner)
    {
        foreach (ulong id in items)
        {
            ledger.TryGetItem(id, out _, out ItemRecord item);
            ledger.PutItem(id, newOwner, item);
        }
    }
}
