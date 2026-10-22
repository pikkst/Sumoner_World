using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Progression;
using SumonerWorld.Core.World;
using SumonerWorld.Net.Messages;
using SumonerWorld.Net.Transport;

namespace SumonerWorld.Net.Server;

/// <summary>
/// Sends each player their pacted creatures (<see cref="CompanionList"/>, M2-06) and their gold and inventory
/// (<see cref="InventoryList"/>, M2-13) whenever either changed since the last send to them, including what the creatures wear
/// and carry (M2-14, M2-14b). A new or resumed connection gets both again.
/// </summary>
internal sealed class CompanionListSender(CreatureWorld world, ITransport transport)
{
    private readonly CompanionListCodec _codec = new();
    private readonly byte[] _buffer = new byte[Protocol.MaxMessageSize];
    private readonly List<Companion> _current = [];
    private readonly Dictionary<ulong, (Companion[] Companions, ulong Gear)> _sent = [];
    private readonly InventoryListCodec _inventoryCodec = new();
    private readonly QuestListSender _quests = new(world, transport);
    private readonly Dictionary<ulong, (int Gold, Core.Items.ItemCount[] Slots, WornItems Worn, int Crafting)> _sentInventory = [];

    /// <summary>Sends the account's list on <paramref name="connection"/> if it changed.</summary>
    public void Update(ConnectionId connection, ulong account)
    {
        UpdateInventory(connection, account);
        _quests.Update(connection, account);
        Companions.Of(world, account, _current);
        ulong gear = GearFingerprint();
        if (_sent.TryGetValue(account, out (Companion[] Companions, ulong Gear) sent) && sent.Gear == gear
            && sent.Companions.AsSpan().SequenceEqual(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_current)))
        {
            return;
        }

        _sent[account] = ([.. _current], gear);
        var entries = new CompanionEntry[Math.Min(_current.Count, CompanionList.MaxEntries)];
        for (int i = 0; i < entries.Length; i++)
        {
            Companion companion = _current[i];
            entries[i] = new CompanionEntry(
                companion.Id.Value,
                (byte)companion.Species,
                companion.Summoned,
                (byte)companion.Level,
                (byte)companion.Stage,
                (byte)companion.BondLevel,
                (byte)Math.Min(companion.Points, byte.MaxValue),
                companion.Learned,
                BranchAffinities(companion),
                (byte)companion.Combos,
                world.TryFindCreature(companion.Id, out Core.Ecs.Entity worn) ? Worn(world.World.GetComponent<Core.Items.Equipment>(worn)) : default,
                world.Economy.CreatureBags.Find(companion.Id.Value) is Core.Items.Inventory bag ? new BagView(bag.Capacity, Entries(bag)) : null);
        }

        var writer = new MessageWriter(_buffer);
        _codec.Write(ref writer, new CompanionList(entries));
        transport.Send(connection, Channel.ReliableOrdered, writer.WrittenSpan);
    }

    // What the player wears and the recipe they are making (+1), from their avatar.
    private (WornItems Worn, int Crafting) WornAndCrafting(ulong account)
    {
        foreach (Core.Ecs.Entity avatar in world.World.Store<PlayerAvatar>().Entities)
        {
            if (world.World.GetComponent<PlayerAvatar>(avatar).AccountId == account)
            {
                int crafting = world.World.Has<Core.Items.Crafting>(avatar) ? world.World.GetComponent<Core.Items.Crafting>(avatar).Recipe + 1 : 0;
                return (Worn(world.World.GetComponent<Core.Items.Equipment>(avatar)), crafting);
            }
        }

        return (default, 0);
    }

    // A number that changes whenever what the current companions wear or carry changes.
    private ulong GearFingerprint()
    {
        ulong hash = 14_695_981_039_346_656_037;
        foreach (Companion companion in _current)
        {
            if (world.TryFindCreature(companion.Id, out Core.Ecs.Entity creature))
            {
                Core.Items.Equipment worn = world.World.GetComponent<Core.Items.Equipment>(creature);
                hash = Mix(Mix(Mix(hash, (ulong)worn.Weapon), (ulong)worn.Head), (ulong)worn.Body);
                hash = Mix(Mix(Mix(hash, (ulong)worn.OffHand), (ulong)worn.Charm1), (ulong)worn.Charm2);
            }

            if (world.Economy.CreatureBags.Find(companion.Id.Value) is Core.Items.Inventory bag)
            {
                foreach (Core.Items.ItemCount stack in bag.Stacks)
                {
                    hash = Mix(Mix(hash, (ulong)stack.Item), (ulong)stack.Count);
                }
            }
        }

        return hash;
    }

    private static ulong Mix(ulong hash, ulong value) => (hash ^ value) * 1_099_511_628_211;

    // The non-empty slots of a bag, each with its slot index.
    private static List<InventoryEntry> Entries(Core.Items.Inventory bag)
    {
        var entries = new List<InventoryEntry>();
        ReadOnlySpan<Core.Items.ItemCount> stacks = bag.Stacks;
        for (int i = 0; i < stacks.Length; i++)
        {
            if (stacks[i].Count > 0)
            {
                entries.Add(new InventoryEntry(stacks[i].Item, stacks[i].Count, i));
            }
        }

        return entries;
    }

    private static WornItems Worn(Core.Items.Equipment equipment) =>
        new((ushort)equipment.Weapon, (ushort)equipment.OffHand, (ushort)equipment.Head, (ushort)equipment.Body, (ushort)equipment.Charm1, (ushort)equipment.Charm2);

    // How much it likes each branch of its tree now (its traits and the roles it did most).
    private Affinities BranchAffinities(Companion companion)
    {
        if (world.Data.SkillTreeOf(companion.Species) is not Core.Data.Definitions.SkillTreeDefinition tree
            || !world.TryFindCreature(companion.Id, out Core.Ecs.Entity creature))
        {
            return default;
        }

        Core.Cognition.Personality personality = world.Minds.GetPersonality(world.World.GetComponent<MindRef>(creature).Handle);
        CreatureGrowth growth = world.World.GetComponent<CreatureGrowth>(creature);
        Span<int> values = stackalloc int[Core.Data.Definitions.SkillTreeDefinition.MaxBranches];
        int count = Math.Min(tree.Branches.Count, values.Length);
        for (int b = 0; b < count; b++)
        {
            values[b] = SkillRules.Affinity(tree.Branches[b], personality, growth);
        }

        return Affinities.Of(values[..count]);
    }

    /// <summary>Forgets what was sent to the account, so the next <see cref="Update"/> sends both again.</summary>
    public void Forget(ulong account)
    {
        _sent.Remove(account);
        _sentInventory.Remove(account);
        _quests.Forget(account);
    }

    private void UpdateInventory(ConnectionId connection, ulong account)
    {
        Core.Items.Inventory inventory = world.Economy.Inventories.Of(account);
        (WornItems worn, int crafting) = WornAndCrafting(account);
        if (_sentInventory.TryGetValue(account, out (int Gold, Core.Items.ItemCount[] Slots, WornItems Worn, int Crafting) sent)
            && sent.Gold == inventory.Gold && inventory.Stacks.SequenceEqual(sent.Slots) && sent.Worn == worn && sent.Crafting == crafting)
        {
            return;
        }

        _sentInventory[account] = (inventory.Gold, inventory.Stacks.ToArray(), worn, crafting);
        List<InventoryEntry> entries = Entries(inventory);
        var writer = new MessageWriter(_buffer);
        _inventoryCodec.Write(ref writer, new InventoryList(inventory.Gold, entries, worn, crafting));
        transport.Send(connection, Channel.ReliableOrdered, writer.WrittenSpan);
    }
}
