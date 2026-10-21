using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Transactions;
using SumonerWorld.Core.World;

namespace SumonerWorld.Core.Items;

/// <summary>Shared helpers of the bag transactions (M2-14b).</summary>
public static class Bags
{
    /// <summary>The player's own bag.</summary>
    public static Inventory OfPlayer(CreatureWorld world, Entity avatar)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.Economy.Inventories.Of(world.World.GetComponent<PlayerAvatar>(avatar).AccountId);
    }

    /// <summary>A creature's bag, sized by its body.</summary>
    public static Inventory OfCreature(CreatureWorld world, Entity creature)
    {
        ArgumentNullException.ThrowIfNull(world);
        BodyType body = world.Data.Species[world.World.GetComponent<SpeciesRef>(creature).Index].BodyType;
        return world.Economy.CreatureBags.Of(world.World.GetComponent<StableId>(creature).Value, body);
    }

    /// <summary>One of the player's own creatures that is summoned at their side, or null.</summary>
    public static Entity? Companion(CreatureWorld world, Entity avatar, StableId creature)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (creature.Value == 0 || Gear.Wearer(world, avatar, creature) is not Entity found)
        {
            return null;
        }

        return world.Services.Bonding.GetBond(world.World.GetComponent<MindRef>(found).Handle).Summoned ? found : null;
    }
}

/// <summary>A player moves a stack to another slot of their own bag: onto an empty slot, onto the same item, or swapping (M2-14b).</summary>
/// <param name="Creatures">The world.</param>
/// <param name="Avatar">The player.</param>
/// <param name="From">Source slot.</param>
/// <param name="To">Target slot.</param>
public record struct MoveItemTransaction(CreatureWorld Creatures, Entity Avatar, int From, int To) : ITransaction
{
    /// <inheritdoc />
    public readonly IntentResult Validate(ulong now)
    {
        ArgumentNullException.ThrowIfNull(Creatures);
        if (!Creatures.World.Store<PlayerAvatar>().Has(Avatar))
        {
            return IntentResult.Reject(IntentRejectReason.InvalidTarget);
        }

        Inventory bag = Bags.OfPlayer(Creatures, Avatar);
        return (uint)From >= (uint)bag.Capacity || (uint)To >= (uint)bag.Capacity || From == To || bag.Stacks[From].Count == 0
            ? IntentResult.Reject(IntentRejectReason.InvalidArguments)
            : IntentResult.Applied();
    }

    /// <inheritdoc />
    public readonly IntentResult Commit(ulong now)
    {
        Bags.OfPlayer(Creatures, Avatar).Move(Creatures.Data, From, To);
        return IntentResult.Applied();
    }
}

/// <summary>
/// A player moves a whole stack between their bag and the bag of one of their creatures at their side (M2-14b): all of it fits
/// or nothing moves.
/// </summary>
/// <param name="Creatures">The world.</param>
/// <param name="Avatar">The player.</param>
/// <param name="Creature">The creature's stable id.</param>
/// <param name="Slot">The source slot (in the player's bag, or the creature's when <paramref name="ToPlayer"/>).</param>
/// <param name="ToPlayer">True to take from the creature, false to give to it.</param>
public record struct TransferItemTransaction(CreatureWorld Creatures, Entity Avatar, StableId Creature, int Slot, bool ToPlayer) : ITransaction
{
    /// <inheritdoc />
    public readonly IntentResult Validate(ulong now)
    {
        ArgumentNullException.ThrowIfNull(Creatures);
        if (!Creatures.World.Store<PlayerAvatar>().Has(Avatar) || Gear.Wearer(Creatures, Avatar, Creature) is not Entity creature || Creature.Value == 0)
        {
            return IntentResult.Reject(IntentRejectReason.InvalidTarget);
        }

        if (Bags.Companion(Creatures, Avatar, Creature) is null)
        {
            return Gear.Refuse(GearRefusal.NotWithYou);
        }

        (Inventory from, Inventory to) = Ends(creature);
        if ((uint)Slot >= (uint)from.Capacity || from.Stacks[Slot].Count == 0)
        {
            return IntentResult.Reject(IntentRejectReason.InvalidArguments);
        }

        return to.CanAdd(Creatures.Data, [from.Stacks[Slot]]) ? IntentResult.Applied() : Gear.Refuse(GearRefusal.InventoryFull);
    }

    /// <inheritdoc />
    public readonly IntentResult Commit(ulong now)
    {
        (Inventory from, Inventory to) = Ends(Gear.Wearer(Creatures, Avatar, Creature)!.Value);
        to.Add(Creatures.Data, [from.TakeSlot(Slot)]);
        return IntentResult.Applied();
    }

    private readonly (Inventory From, Inventory To) Ends(Entity creature)
    {
        Inventory player = Bags.OfPlayer(Creatures, Avatar);
        Inventory pet = Bags.OfCreature(Creatures, creature);
        return ToPlayer ? (pet, player) : (player, pet);
    }
}

/// <summary>
/// A player uses one item from a slot of their bag (M2-14b): food and potions give back health, stamina or mana, to themselves
/// or to one of their creatures at their side. Not while in a move, and not on someone who is down.
/// </summary>
/// <param name="Creatures">The world.</param>
/// <param name="Avatar">The player.</param>
/// <param name="Target">0 for the player, else their creature's stable id.</param>
/// <param name="Slot">Slot in the player's bag.</param>
public record struct UseItemTransaction(CreatureWorld Creatures, Entity Avatar, StableId Target, int Slot) : ITransaction
{
    /// <inheritdoc />
    public readonly IntentResult Validate(ulong now)
    {
        ArgumentNullException.ThrowIfNull(Creatures);
        if (!Creatures.World.Store<PlayerAvatar>().Has(Avatar) || Gear.Wearer(Creatures, Avatar, Target) is not Entity target)
        {
            return IntentResult.Reject(IntentRejectReason.InvalidTarget);
        }

        if (Target.Value != 0 && Bags.Companion(Creatures, Avatar, Target) is null)
        {
            return Gear.Refuse(GearRefusal.NotWithYou);
        }

        Inventory bag = Bags.OfPlayer(Creatures, Avatar);
        if ((uint)Slot >= (uint)bag.Capacity || bag.Stacks[Slot].Count == 0)
        {
            return IntentResult.Reject(IntentRejectReason.InvalidArguments);
        }

        if (!Creatures.Data.Items[bag.Stacks[Slot].Item].Restore.IsUsable)
        {
            return Gear.Refuse(GearRefusal.NotUsable);
        }

        if (Creatures.World.Has<ActiveMove>(Avatar))
        {
            return Gear.Refuse(GearRefusal.Busy);
        }

        return Creatures.World.GetComponent<Combatant>(target).Health <= 0 ? Gear.Refuse(GearRefusal.Down) : IntentResult.Applied();
    }

    /// <inheritdoc />
    public readonly IntentResult Commit(ulong now)
    {
        Entity target = Gear.Wearer(Creatures, Avatar, Target)!.Value;
        Inventory bag = Bags.OfPlayer(Creatures, Avatar);
        ItemRestore restore = Creatures.Data.Items[bag.Stacks[Slot].Item].Restore;
        bag.TakeOne(Slot);
        ref Combatant combatant = ref Creatures.World.GetComponent<Combatant>(target);
        combatant.Health = Math.Min(combatant.MaxHealth, combatant.Health + restore.Health);
        combatant.Stamina = Math.Min(combatant.MaxStamina, combatant.Stamina + restore.Stamina);
        combatant.Mana = Math.Min(combatant.MaxMana, combatant.Mana + restore.Mana);
        return IntentResult.Applied();
    }
}
