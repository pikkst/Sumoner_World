using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Progression;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.Transactions;
using SumonerWorld.Core.World;

namespace SumonerWorld.Core.Items;

/// <summary>Why a craft or equip intent was refused, in <see cref="IntentResult.Detail"/> of a <see cref="IntentRejectReason.WrongState"/>.</summary>
public enum GearRefusal : byte
{
    /// <summary>No detail.</summary>
    None = 0,

    /// <summary>Missing inputs.</summary>
    MissingInputs = 1,

    /// <summary>The result would not fit.</summary>
    InventoryFull = 2,

    /// <summary>The recipe needs a station the slice does not have yet.</summary>
    NeedsStation = 3,

    /// <summary>Busy (already making something, or in a fight move).</summary>
    Busy = 4,

    /// <summary>It is not gear, or this wearer cannot wear it.</summary>
    CannotWear = 5,

    /// <summary>The wearer's level is too low.</summary>
    LevelTooLow = 6,

    /// <summary>The item cannot be used (M2-14b).</summary>
    NotUsable = 7,

    /// <summary>The creature is not at the player's side (M2-14b).</summary>
    NotWithYou = 8,

    /// <summary>The one it is for is down (M2-14b).</summary>
    Down = 9,
}

/// <summary>Shared helpers of the gear transactions.</summary>
internal static class Gear
{
    public static IntentResult Refuse(GearRefusal refusal) => new(IntentStatus.Rejected, IntentRejectReason.WrongState, (byte)refusal);

    // The wearer: the player (id 0) or one of their own pacted creatures.
    public static Entity? Wearer(CreatureWorld world, Entity avatar, StableId wearer)
    {
        if (wearer.Value == 0)
        {
            return avatar;
        }

        if (!world.TryFindCreature(wearer, out Entity creature))
        {
            return null;
        }

        BondState bond = world.Services.Bonding.GetBond(world.World.GetComponent<MindRef>(creature).Handle);
        return bond.State == OwnershipState.Pacted && bond.Owner == world.World.GetComponent<PlayerAvatar>(avatar).AccountId ? creature : null;
    }

    public static int LevelOf(CreatureWorld world, Entity wearer) =>
        world.World.Has<CreatureGrowth>(wearer) ? world.World.GetComponent<CreatureGrowth>(wearer).Level : world.World.GetComponent<PlayerProgress>(wearer).Level;

    public static void Restat(CreatureWorld world, Entity wearer)
    {
        if (world.World.Has<PlayerAvatar>(wearer))
        {
            PlayerStats.Restat(world, wearer);
            return;
        }

        ushort species = world.World.GetComponent<SpeciesRef>(wearer).Index;
        ProgressionComponents.Restat(world.Growth, world.Data, world.World, wearer, world.Data.Species[species].Combat, ref world.World.GetComponent<Combatant>(wearer));
    }
}

/// <summary>
/// A player puts on gear from their inventory (crafting spec §8, M2-14), on themselves or one of their own creatures: it must
/// be gear that wearer can wear at its level; what was worn there goes back into the inventory. Atomic and idempotent; the
/// wearer's stats follow.
/// </summary>
/// <param name="Creatures">The world.</param>
/// <param name="Avatar">The player.</param>
/// <param name="Wearer">0 for the player, else their creature's stable id.</param>
/// <param name="Item">Item index; one of them must be in the inventory.</param>
public record struct EquipTransaction(CreatureWorld Creatures, Entity Avatar, StableId Wearer, int Item) : ITransaction
{
    /// <inheritdoc />
    public readonly IntentResult Validate(ulong now)
    {
        ArgumentNullException.ThrowIfNull(Creatures);
        if (!Creatures.World.Store<PlayerAvatar>().Has(Avatar) || Gear.Wearer(Creatures, Avatar, Wearer) is not Entity wearer)
        {
            return IntentResult.Reject(IntentRejectReason.InvalidTarget);
        }

        Inventory inventory = Creatures.Economy.Inventories.Of(Creatures.World.GetComponent<PlayerAvatar>(Avatar).AccountId);
        if ((uint)Item >= (uint)Creatures.Data.Items.Count || inventory.CountOf(Item) == 0)
        {
            return IntentResult.Reject(IntentRejectReason.InvalidArguments);
        }

        ItemDefinition item = Creatures.Data.Items[Item];
        string kind = EquipRules.WearerOf(Creatures, wearer);
        if (!item.Wearers.Contains(kind) || EquipRules.PositionFor(kind, item.Slot, Creatures.World.GetComponent<Equipment>(wearer)) is null)
        {
            return Gear.Refuse(GearRefusal.CannotWear);
        }

        return Gear.LevelOf(Creatures, wearer) < item.MinLevel ? Gear.Refuse(GearRefusal.LevelTooLow) : IntentResult.Applied();
    }

    /// <inheritdoc />
    public readonly IntentResult Commit(ulong now)
    {
        Entity wearer = Gear.Wearer(Creatures, Avatar, Wearer)!.Value;
        Inventory inventory = Creatures.Economy.Inventories.Of(Creatures.World.GetComponent<PlayerAvatar>(Avatar).AccountId);
        int item = Item;
        ref Equipment worn = ref Creatures.World.GetComponent<Equipment>(wearer);
        EquipPosition position = EquipRules.PositionFor(EquipRules.WearerOf(Creatures, wearer), Creatures.Data.Items[item].Slot, worn)!.Value;
        int old = worn.ItemAt(position);
        inventory.Remove(item, 1);
        if (old >= 0)
        {
            inventory.Add(Creatures.Data, [new ItemCount(old, 1)]); // the slot just freed always takes it
        }

        worn = worn.With(position, item);
        Gear.Restat(Creatures, wearer);
        return IntentResult.Applied((byte)position);
    }
}

/// <summary>A player takes gear off themselves or one of their creatures, back into the inventory if it fits (M2-14).</summary>
/// <param name="Creatures">The world.</param>
/// <param name="Avatar">The player.</param>
/// <param name="Wearer">0 for the player, else their creature's stable id.</param>
/// <param name="Position">The equipment position.</param>
public record struct UnequipTransaction(CreatureWorld Creatures, Entity Avatar, StableId Wearer, EquipPosition Position) : ITransaction
{
    /// <inheritdoc />
    public readonly IntentResult Validate(ulong now)
    {
        ArgumentNullException.ThrowIfNull(Creatures);
        if (!Creatures.World.Store<PlayerAvatar>().Has(Avatar) || Gear.Wearer(Creatures, Avatar, Wearer) is not Entity wearer)
        {
            return IntentResult.Reject(IntentRejectReason.InvalidTarget);
        }

        if ((uint)Position >= Equipment.Positions || Creatures.World.GetComponent<Equipment>(wearer).ItemAt(Position) is int item and < 0)
        {
            return IntentResult.Reject(IntentRejectReason.InvalidArguments);
        }

        Inventory inventory = Creatures.Economy.Inventories.Of(Creatures.World.GetComponent<PlayerAvatar>(Avatar).AccountId);
        return inventory.CanAdd(Creatures.Data, [new ItemCount(Creatures.World.GetComponent<Equipment>(wearer).ItemAt(Position), 1)])
            ? IntentResult.Applied()
            : Gear.Refuse(GearRefusal.InventoryFull);
    }

    /// <inheritdoc />
    public readonly IntentResult Commit(ulong now)
    {
        Entity wearer = Gear.Wearer(Creatures, Avatar, Wearer)!.Value;
        ref Equipment worn = ref Creatures.World.GetComponent<Equipment>(wearer);
        int item = worn.ItemAt(Position);
        Creatures.Economy.Inventories.Of(Creatures.World.GetComponent<PlayerAvatar>(Avatar).AccountId).Add(Creatures.Data, [new ItemCount(item, 1)]);
        worn = worn.With(Position, -1);
        Gear.Restat(Creatures, wearer);
        return IntentResult.Applied();
    }
}

/// <summary>
/// A player starts making a recipe (crafting spec §6, M2-14): it must be made by hand (the slice has no stations yet), the inputs
/// must be there, the result must fit, and the player must not be busy. Nothing is taken yet: <see cref="CraftingSystem"/>
/// finishes it after the recipe's time.
/// </summary>
/// <param name="Creatures">The world.</param>
/// <param name="Avatar">The player.</param>
/// <param name="Recipe">Index in the recipes data.</param>
public record struct CraftTransaction(CreatureWorld Creatures, Entity Avatar, int Recipe) : ITransaction
{
    /// <inheritdoc />
    public readonly IntentResult Validate(ulong now)
    {
        ArgumentNullException.ThrowIfNull(Creatures);
        EcsWorld world = Creatures.World;
        if (!world.Store<PlayerAvatar>().Has(Avatar))
        {
            return IntentResult.Reject(IntentRejectReason.InvalidTarget);
        }

        if ((uint)Recipe >= (uint)Creatures.Data.Recipes.Count)
        {
            return IntentResult.Reject(IntentRejectReason.InvalidArguments);
        }

        RecipeDefinition recipe = Creatures.Data.Recipes[Recipe];
        if (recipe.Station != CraftStation.Hand)
        {
            return Gear.Refuse(GearRefusal.NeedsStation);
        }

        if (world.Has<Crafting>(Avatar) || world.Has<ActiveMove>(Avatar) || world.Has<SigilCast>(Avatar))
        {
            return Gear.Refuse(GearRefusal.Busy);
        }

        return CraftingSystem.Check(Creatures, world.GetComponent<PlayerAvatar>(Avatar).AccountId, recipe) is GearRefusal refusal and not GearRefusal.None
            ? Gear.Refuse(refusal)
            : IntentResult.Applied();
    }

    /// <inheritdoc />
    public readonly IntentResult Commit(ulong now)
    {
        Creatures.World.Add(Avatar, new Crafting(now, Recipe, 0));
        return IntentResult.Applied();
    }
}

/// <summary>
/// Finishes crafting (crafting spec §6): after the recipe's time the inputs are taken and the result is given in one step, if they
/// are still there and it still fits; a hit on the crafter, or a move it starts, interrupts it (nothing is taken).
/// </summary>
public sealed class CraftingSystem : ISimulationSystem
{
    private readonly CreatureWorld _world;
    private readonly ComponentStore<Crafting> _crafting;
    private readonly ComponentStore<PlayerAvatar> _players;

    /// <summary>Creates the system for a world.</summary>
    public CraftingSystem(CreatureWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
        _crafting = world.World.Store<Crafting>();
        _players = world.World.Store<PlayerAvatar>();
    }

    /// <summary>Items made since the start.</summary>
    public int Made { get; private set; }

    /// <summary>Crafts interrupted since the start.</summary>
    public int Interrupted { get; private set; }

    /// <inheritdoc />
    public SimulationStage Stage => SimulationStage.StatusEffects;

    /// <summary>Why a recipe cannot be made from this inventory now (<see cref="GearRefusal.None"/> when it can).</summary>
    public static GearRefusal Check(CreatureWorld world, ulong account, RecipeDefinition recipe)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(recipe);
        Inventory inventory = world.Economy.Inventories.Of(account);
        foreach (RecipePart part in recipe.Inputs)
        {
            if (inventory.CountOf(world.Data.ItemIndex(part.Item)) < part.Count)
            {
                return GearRefusal.MissingInputs;
            }
        }

        return inventory.CanAdd(world.Data, [new ItemCount(world.Data.ItemIndex(recipe.Output.Item), recipe.Output.Count)]) ? GearRefusal.None : GearRefusal.InventoryFull;
    }

    /// <inheritdoc />
    public void Execute(in SimulationTick tick)
    {
        if (_crafting.Count == 0)
        {
            return;
        }

        foreach (CombatHit hit in _world.Events.Read<CombatHit>())
        {
            if (_crafting.Has(hit.Target))
            {
                _world.World.Remove<Crafting>(hit.Target);
                Interrupted++;
            }
        }

        ReadOnlySpan<Entity> crafters = _crafting.Entities;
        for (int i = crafters.Length - 1; i >= 0; i--)
        {
            Entity crafter = crafters[i];
            Crafting crafting = _crafting.Components[i];
            if (_world.World.Has<ActiveMove>(crafter))
            {
                _world.World.Remove<Crafting>(crafter);
                Interrupted++;
                continue;
            }

            RecipeDefinition recipe = _world.Data.Recipes[crafting.Recipe];
            if (tick.Index - crafting.Since < (ulong)recipe.Ticks)
            {
                continue;
            }

            _world.World.Remove<Crafting>(crafter);
            Finish(_players.Get(crafter).AccountId, recipe);
        }
    }

    private void Finish(ulong account, RecipeDefinition recipe)
    {
        if (Check(_world, account, recipe) != GearRefusal.None)
        {
            Interrupted++;
            return;
        }

        Inventory inventory = _world.Economy.Inventories.Of(account);
        foreach (RecipePart part in recipe.Inputs)
        {
            inventory.Remove(_world.Data.ItemIndex(part.Item), part.Count);
        }

        int output = _world.Data.ItemIndex(recipe.Output.Item);
        inventory.Add(_world.Data, [new ItemCount(output, recipe.Output.Count)]);
        _world.Events.Publish(new Quests.QuestEvent(account, ObjectiveType.Craft, output, recipe.Output.Count));
        Made++;
    }
}
