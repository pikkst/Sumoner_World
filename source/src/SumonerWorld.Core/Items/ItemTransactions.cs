using System.Numerics;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Transactions;
using SumonerWorld.Core.World;

namespace SumonerWorld.Core.Items;

/// <summary>Why an item intent was refused, in <see cref="IntentResult.Detail"/> of a <see cref="IntentRejectReason.WrongState"/>.</summary>
public enum ItemRefusal : byte
{
    /// <summary>No detail.</summary>
    None = 0,

    /// <summary>The inventory is full (slots or weight).</summary>
    InventoryFull = 1,

    /// <summary>The node was gathered and has not regrown yet.</summary>
    NotReady = 2,
}

/// <summary>
/// A player picks up their own loot bag (crafting spec §4, M2-13): within <see cref="Range"/>, and only if everything fits.
/// Atomic and idempotent (R27, R34): the gold and items move into the inventory and the bag is gone, or nothing changes; a
/// repeated intent gets the first answer. <see cref="IntentResult.Detail"/> is the gold picked up (at most 255 shown).
/// </summary>
/// <param name="Creatures">The world.</param>
/// <param name="Avatar">The player.</param>
/// <param name="Bag">The bag's id.</param>
public record struct PickUpTransaction(CreatureWorld Creatures, Entity Avatar, ulong Bag) : ITransaction
{
    /// <summary>Farthest the bag may be, in metres.</summary>
    public const float Range = 3f;

    /// <inheritdoc />
    public readonly IntentResult Validate(ulong now)
    {
        ArgumentNullException.ThrowIfNull(Creatures);
        EcsWorld world = Creatures.World;
        if (!world.Store<PlayerAvatar>().Has(Avatar) || Creatures.Economy.BagWithId(Bag) is not LootBag bag)
        {
            return IntentResult.Reject(IntentRejectReason.InvalidTarget);
        }

        ulong account = world.GetComponent<PlayerAvatar>(Avatar).AccountId;
        if (bag.Owner != account)
        {
            return IntentResult.Reject(IntentRejectReason.NotOwner);
        }

        if (Vector3.Distance(world.GetComponent<Position>(Avatar).Value, bag.Position) > Range)
        {
            return IntentResult.Reject(IntentRejectReason.OutOfRange);
        }

        return Creatures.Economy.Inventories.Of(account).CanAdd(Creatures.Data, bag.Items)
            ? IntentResult.Applied()
            : new IntentResult(IntentStatus.Rejected, IntentRejectReason.WrongState, (byte)ItemRefusal.InventoryFull);
    }

    /// <inheritdoc />
    public readonly IntentResult Commit(ulong now)
    {
        LootBag bag = Creatures.Economy.BagWithId(Bag)!;
        Inventory inventory = Creatures.Economy.Inventories.Of(bag.Owner);
        inventory.Add(Creatures.Data, bag.Items);
        inventory.AddGold(bag.Gold);
        Creatures.Economy.Remove(bag);
        return IntentResult.Applied((byte)Math.Min(bag.Gold, byte.MaxValue));
    }
}

/// <summary>
/// A player gathers from a node (crafting spec §5, M2-13): within <see cref="Range"/>, when it has regrown and the most it can
/// give fits. The amount is rolled with the world's seeded random stream; the node then regrows. Atomic and idempotent.
/// <see cref="IntentResult.Detail"/> is how many were gathered.
/// </summary>
/// <param name="Creatures">The world.</param>
/// <param name="Avatar">The player.</param>
/// <param name="Node">The node's id.</param>
public record struct GatherTransaction(CreatureWorld Creatures, Entity Avatar, ulong Node) : ITransaction
{
    /// <summary>Farthest the node may be, in metres.</summary>
    public const float Range = 3f;

    /// <inheritdoc />
    public readonly IntentResult Validate(ulong now)
    {
        ArgumentNullException.ThrowIfNull(Creatures);
        EcsWorld world = Creatures.World;
        if (!world.Store<PlayerAvatar>().Has(Avatar) || Creatures.Economy.NodeWithId(Node) is not GatherNode node)
        {
            return IntentResult.Reject(IntentRejectReason.InvalidTarget);
        }

        if (Vector3.Distance(world.GetComponent<Position>(Avatar).Value, node.Position) > Range)
        {
            return IntentResult.Reject(IntentRejectReason.OutOfRange);
        }

        if (!node.IsReady(now))
        {
            return new IntentResult(IntentStatus.Rejected, IntentRejectReason.WrongState, (byte)ItemRefusal.NotReady);
        }

        GatherNodeDefinition kind = Creatures.Data.GatherNodes[node.Kind];
        ItemCount most = new(Creatures.Data.ItemIndex(kind.Item), kind.Max);
        return Creatures.Economy.Inventories.Of(world.GetComponent<PlayerAvatar>(Avatar).AccountId).CanAdd(Creatures.Data, [most])
            ? IntentResult.Applied()
            : new IntentResult(IntentStatus.Rejected, IntentRejectReason.WrongState, (byte)ItemRefusal.InventoryFull);
    }

    /// <inheritdoc />
    public readonly IntentResult Commit(ulong now)
    {
        GatherNode node = Creatures.Economy.NodeWithId(Node)!;
        GatherNodeDefinition kind = Creatures.Data.GatherNodes[node.Kind];
        int count = Creatures.Random.NextInt(kind.Min, kind.Max + 1);
        ulong account = Creatures.World.GetComponent<PlayerAvatar>(Avatar).AccountId;
        Creatures.Economy.Inventories.Of(account).Add(Creatures.Data, [new ItemCount(Creatures.Data.ItemIndex(kind.Item), count)]);
        node.ReadyAt = now + (ulong)kind.RegrowTicks;
        Creatures.Events.Publish(new Quests.QuestEvent(account, ObjectiveType.Gather, Creatures.Data.ItemIndex(kind.Item), count));
        return IntentResult.Applied((byte)count);
    }
}
