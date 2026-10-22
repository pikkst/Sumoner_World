using System.Numerics;
using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Data;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Core.Randomness;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.World;

namespace SumonerWorld.Core.Items;

/// <summary>The players who fought a creature (up to four accounts), so each gets their own loot (crafting spec §4). No padding.</summary>
/// <param name="A">First.</param>
/// <param name="B">Second.</param>
/// <param name="C">Third.</param>
/// <param name="D">Fourth.</param>
public record struct Fighters(ulong A, ulong B, ulong C, ulong D)
{
    /// <summary>The same with <paramref name="account"/> added (no change when it is there already or all four places are taken).</summary>
    public readonly Fighters With(ulong account) =>
        account == 0 || account == A || account == B || account == C || account == D ? this
        : A == 0 ? this with { A = account }
        : B == 0 ? this with { B = account }
        : C == 0 ? this with { C = account }
        : D == 0 ? this with { D = account }
        : this;

    /// <summary>The account at place 0–3 (0 = none).</summary>
    public readonly ulong this[int index] => index switch
    {
        0 => A,
        1 => B,
        2 => C,
        _ => D,
    };
}

/// <summary>A personal loot bag (crafting spec §4): only its owner sees and picks it up; it vanishes after five real minutes.</summary>
public sealed class LootBag
{
    /// <summary>Its id (what the owner's client names it by).</summary>
    public required ulong Id { get; init; }

    /// <summary>The account it belongs to.</summary>
    public required ulong Owner { get; init; }

    /// <summary>Where it lies.</summary>
    public required Vector3 Position { get; init; }

    /// <summary>Tick it vanishes.</summary>
    public required ulong ExpiresAt { get; init; }

    /// <summary>Gold in it.</summary>
    public required int Gold { get; init; }

    /// <summary>Items in it.</summary>
    public required IReadOnlyList<ItemCount> Items { get; init; }
}

/// <summary>A gathering node (crafting spec §5): a bush, a root patch or fallen branches; it regrows after it is gathered.</summary>
public sealed class GatherNode
{
    /// <summary>Its id.</summary>
    public required ulong Id { get; init; }

    /// <summary>Index into the gathering nodes data.</summary>
    public required int Kind { get; init; }

    /// <summary>Where it is.</summary>
    public required Vector3 Position { get; init; }

    /// <summary>Tick from which it can be gathered (0 = now).</summary>
    public ulong ReadyAt { get; set; }

    /// <summary>True when it can be gathered at <paramref name="now"/>.</summary>
    public bool IsReady(ulong now) => now >= ReadyAt;
}

/// <summary>
/// The world's items outside inventories (M2-13): every player's <see cref="InventoryStore"/>, the loot bags lying around and the
/// gathering nodes. Server state; bags and nodes are not saved (bags last minutes; nodes are placed again from the same seed).
/// </summary>
public sealed class Economy
{
    /// <summary>How long a loot bag lies before it vanishes (five real minutes at 30 Hz).</summary>
    public const ulong BagTicks = 9_000;

    private readonly List<LootBag> _bags = [];
    private readonly List<GatherNode> _nodes = [];
    private ulong _nextId;

    /// <summary>Inventories.</summary>
    public InventoryStore Inventories { get; } = new();

    /// <summary>Pacted creatures' bags (M2-14b).</summary>
    public CreatureBags CreatureBags { get; } = new();

    /// <summary>Every player's quests (M2-19).</summary>
    public Quests.QuestBook Quests { get; } = new();

    /// <summary>Registers the events of player progress (quest objectives) with a world's event bus.</summary>
    public static void RegisterEvents(Events.EventBus events, int capacity)
    {
        ArgumentNullException.ThrowIfNull(events);
        events.Register<Quests.QuestEvent>(capacity);
    }

    /// <summary>Loot bags lying in the world.</summary>
    public IReadOnlyList<LootBag> Bags => _bags;

    /// <summary>Gathering nodes.</summary>
    public IReadOnlyList<GatherNode> Nodes => _nodes;

    /// <summary>Drops a bag; returns it.</summary>
    public LootBag Drop(ulong owner, Vector3 at, int gold, IReadOnlyList<ItemCount> items, ulong now)
    {
        var bag = new LootBag { Id = ++_nextId, Owner = owner, Position = at, ExpiresAt = now + BagTicks, Gold = gold, Items = items };
        _bags.Add(bag);
        return bag;
    }

    /// <summary>The bag with this id, or null.</summary>
    public LootBag? BagWithId(ulong id) => _bags.Find(bag => bag.Id == id);

    /// <summary>Removes a bag (picked up or gone).</summary>
    public void Remove(LootBag bag) => _bags.Remove(bag);

    /// <summary>Removes bags whose time is up.</summary>
    public void Expire(ulong now)
    {
        for (int i = _bags.Count - 1; i >= 0; i--)
        {
            if (now >= _bags[i].ExpiresAt)
            {
                _bags.RemoveAt(i);
            }
        }
    }

    /// <summary>Adds a gathering node; returns it.</summary>
    public GatherNode AddNode(int kind, Vector3 at)
    {
        var node = new GatherNode { Id = ++_nextId, Kind = kind, Position = at };
        _nodes.Add(node);
        return node;
    }

    /// <summary>The node with this id, or null.</summary>
    public GatherNode? NodeWithId(ulong id) => _nodes.Find(node => node.Id == id);

    /// <summary>
    /// Places <paramref name="perKind"/> nodes of every kind in a ring 8–30 m around <paramref name="centre"/>, from
    /// <paramref name="seed"/> (the same seed places them the same way after a restart).
    /// </summary>
    public void PlaceNodes(IDataRegistry data, Heightfield? ground, Vector3 centre, int perKind, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(data);
        var random = new SeededRandom(seed);
        for (int kind = 0; kind < data.GatherNodes.Count; kind++)
        {
            for (int i = 0; i < perKind; i++)
            {
                float angle = random.NextInt(0, 360) * MathF.PI / 180f;
                float distance = random.NextInt(8, 31);
                Vector3 at = centre + new Vector3(MathF.Sin(angle) * distance, 0, MathF.Cos(angle) * distance);
                at.Y = ground?.HeightAt(at.X, at.Z) ?? centre.Y;
                AddNode(kind, at);
            }
        }
    }

    /// <summary>The account's inventory for saving.</summary>
    public InventorySave Export(ulong account, IDataRegistry data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Inventory inventory = Inventories.Of(account);
        return new InventorySave(inventory.Gold, Export(inventory, data));
    }

    /// <summary>A bag's slots by item id, in slot order (empty slots as an empty id, trailing ones left out), for saving.</summary>
    public static IReadOnlyList<SavedItem> Export(Inventory bag, IDataRegistry data)
    {
        ArgumentNullException.ThrowIfNull(bag);
        ArgumentNullException.ThrowIfNull(data);
        var items = new List<SavedItem>();
        ReadOnlySpan<ItemCount> stacks = bag.Stacks;
        int last = stacks.Length - 1;
        while (last >= 0 && stacks[last].Count == 0)
        {
            last--;
        }

        for (int i = 0; i <= last; i++)
        {
            items.Add(stacks[i].Count > 0 ? new SavedItem(data.Items[stacks[i].Item].Id, stacks[i].Count) : new SavedItem(string.Empty, 0));
        }

        return items;
    }

    /// <summary>Fills a bag from saved slots, each in its place; items the data no longer knows leave their slot empty.</summary>
    public static void Import(Inventory bag, int gold, IReadOnlyList<SavedItem> saved, IDataRegistry data)
    {
        ArgumentNullException.ThrowIfNull(bag);
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(data);
        var slots = new List<ItemCount>();
        foreach (SavedItem item in saved)
        {
            slots.Add(item.Count > 0 && data.Items.Any(definition => definition.Id == item.Item) ? new ItemCount(data.ItemIndex(item.Item), item.Count) : default);
        }

        bag.Restore(gold, slots);
    }

    /// <summary>Restores a saved inventory; items the data no longer knows are dropped.</summary>
    public void Import(ulong account, InventorySave saved, IDataRegistry data)
    {
        ArgumentNullException.ThrowIfNull(saved);
        Import(Inventories.Of(account), saved.Gold, saved.Items, data);
    }

    /// <summary>Adds inventories, creature bags, loot bags and nodes to a state hash.</summary>
    public void AddTo(ref StateHash hash)
    {
        Inventories.AddTo(ref hash);
        CreatureBags.AddTo(ref hash);
        Quests.AddTo(ref hash);
        foreach (LootBag bag in _bags)
        {
            hash.Add(bag.Id);
            hash.Add(bag.Owner);
            hash.Add((ulong)bag.Gold);
        }

        foreach (GatherNode node in _nodes)
        {
            hash.Add(node.Id);
            hash.Add(node.ReadyAt);
        }
    }
}

/// <summary>
/// Loot (crafting spec §4, M2-13): when a wild creature is defeated, every player who fought it gets their own bag where it fell,
/// rolled from its species' loot table with the world's seeded random stream (R11); bags vanish after
/// <see cref="Economy.BagTicks"/>. Killing still has its memory consequences (combat spec §6); a spared creature drops nothing.
/// </summary>
public sealed class LootSystem : ISimulationSystem
{
    private const int Fighters = 4;
    private const int PerMille = 1000;
    private const float SpreadMetres = 0.8f;

    private readonly CreatureWorld _world;

    /// <summary>Creates the system for a world.</summary>
    public LootSystem(CreatureWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
    }

    /// <summary>Bags dropped since the start.</summary>
    public int Dropped { get; private set; }

    /// <inheritdoc />
    public SimulationStage Stage => SimulationStage.StatusEffects;

    /// <inheritdoc />
    public void Execute(in SimulationTick tick)
    {
        Economy economy = _world.Economy;
        economy.Expire(tick.Index);
        foreach (CombatantDefeated defeated in _world.Events.Read<CombatantDefeated>())
        {
            if (defeated.Species == ushort.MaxValue || _world.Data.LootTableOf(defeated.Species) is not LootTableDefinition table)
            {
                continue;
            }

            for (int i = 0; i < Fighters; i++)
            {
                if (defeated.Fighters[i] is ulong account and not 0)
                {
                    var spread = new Vector3((i - 1.5f) * SpreadMetres, 0, 0);
                    economy.Drop(account, defeated.At + spread, Roll(table, out List<ItemCount> items), items, tick.Index);
                    Dropped++;
                }
            }
        }
    }

    private int Roll(LootTableDefinition table, out List<ItemCount> items)
    {
        var random = _world.Random;
        items = [];
        foreach (LootEntryDefinition entry in table.Entries)
        {
            if (random.NextInt(0, PerMille) < entry.ChancePerMille)
            {
                items.Add(new ItemCount(_world.Data.ItemIndex(entry.Item), random.NextInt(entry.Min, entry.Max + 1)));
            }
        }

        return random.NextInt(table.GoldMin, table.GoldMax + 1);
    }
}
