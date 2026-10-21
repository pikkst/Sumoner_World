using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Data;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Progression;
using SumonerWorld.Core.World;

namespace SumonerWorld.Core.Items;

/// <summary>Equipment positions, in the order <see cref="Equipment"/> holds them. Values are part of the protocol.</summary>
public enum EquipPosition
{
    /// <summary>Weapon.</summary>
    Weapon = 0,

    /// <summary>Off hand.</summary>
    OffHand = 1,

    /// <summary>Head.</summary>
    Head = 2,

    /// <summary>Body.</summary>
    Body = 3,

    /// <summary>First charm.</summary>
    Charm1 = 4,

    /// <summary>Second charm (players only).</summary>
    Charm2 = 5,
}

/// <summary>
/// What a player or creature wears (crafting spec §8, companions spec §6, M2-14): item index + 1 per position, 0 = empty.
/// Creatures use only the positions their body type has. No padding (hashed raw).
/// </summary>
/// <param name="Weapon">Weapon.</param>
/// <param name="OffHand">Off hand.</param>
/// <param name="Head">Head.</param>
/// <param name="Body">Body.</param>
/// <param name="Charm1">First charm.</param>
/// <param name="Charm2">Second charm.</param>
public record struct Equipment(int Weapon, int OffHand, int Head, int Body, int Charm1, int Charm2)
{
    /// <summary>Positions.</summary>
    public const int Positions = 6;

    /// <summary>The item worn at a position (−1 when empty).</summary>
    public readonly int ItemAt(EquipPosition position) => position switch
    {
        EquipPosition.Weapon => Weapon,
        EquipPosition.OffHand => OffHand,
        EquipPosition.Head => Head,
        EquipPosition.Body => Body,
        EquipPosition.Charm1 => Charm1,
        _ => Charm2,
    } - 1;

    /// <summary>The same with <paramref name="item"/> (−1 = empty) at a position.</summary>
    public readonly Equipment With(EquipPosition position, int item) => position switch
    {
        EquipPosition.Weapon => this with { Weapon = item + 1 },
        EquipPosition.OffHand => this with { OffHand = item + 1 },
        EquipPosition.Head => this with { Head = item + 1 },
        EquipPosition.Body => this with { Body = item + 1 },
        EquipPosition.Charm1 => this with { Charm1 = item + 1 },
        _ => this with { Charm2 = item + 1 },
    };
}

/// <summary>Who wears what where (crafting spec §8, companions spec §6), and the stats gear gives.</summary>
public static class EquipRules
{
    private const int Percent = 100;

    /// <summary>The wearer name of a player (<c>player</c>) or a creature's body type (<c>quadruped</c>, …).</summary>
    public static string WearerOf(CreatureWorld world, Entity entity)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.World.Has<SpeciesRef>(entity)
            ? world.Data.Species[world.World.GetComponent<SpeciesRef>(entity).Index].BodyType.ToString().ToLowerInvariant()
            : "player";
    }

    /// <summary>True when a wearer has this position: players all six; quadrupeds head, body, charm; birds head, charm; …</summary>
    public static bool Has(string wearer, EquipPosition position) => wearer switch
    {
        "player" => true,
        "quadruped" => position is EquipPosition.Head or EquipPosition.Body or EquipPosition.Charm1,
        "avian" => position is EquipPosition.Head or EquipPosition.Charm1,
        "biped" => position is EquipPosition.Weapon or EquipPosition.Head or EquipPosition.Body or EquipPosition.Charm1,
        _ => position is EquipPosition.Body or EquipPosition.Charm1,
    };

    /// <summary>The position a slot kind goes to on this wearer (a second charm when the first is taken), or null.</summary>
    public static EquipPosition? PositionFor(string wearer, EquipSlot slot, in Equipment worn)
    {
        EquipPosition position = slot switch
        {
            EquipSlot.Weapon => EquipPosition.Weapon,
            EquipSlot.OffHand => EquipPosition.OffHand,
            EquipSlot.Head => EquipPosition.Head,
            EquipSlot.Body => EquipPosition.Body,
            _ => worn.Charm1 != 0 && Has(wearer, EquipPosition.Charm2) ? EquipPosition.Charm2 : EquipPosition.Charm1,
        };
        return slot != EquipSlot.None && Has(wearer, position) ? position : null;
    }

    /// <summary>Adds what the worn gear gives to stats already set from the base, level, stage and skills.</summary>
    public static void Apply(IDataRegistry data, in Equipment worn, ref Combatant combatant)
    {
        ArgumentNullException.ThrowIfNull(data);
        int power = 0;
        for (int i = 0; i < Equipment.Positions; i++)
        {
            int item = worn.ItemAt((EquipPosition)i);
            if (item < 0 || item >= data.Items.Count)
            {
                continue;
            }

            GearStats stats = data.Items[item].Stats;
            power += stats.Power;
            combatant.Guard += stats.Guard;
            combatant.ResistPhysical += stats.ResistPhysical;
            combatant.ResistPrimal += stats.ResistPrimal;
            combatant.MaxStamina += stats.Stamina;
        }

        combatant.Power = combatant.Power * (Percent + power) / Percent;
    }

    /// <summary>The worn items by id, in position order (empty strings for empty positions), for saving.</summary>
    public static IReadOnlyList<string> Export(IDataRegistry data, in Equipment worn)
    {
        ArgumentNullException.ThrowIfNull(data);
        var ids = new string[Equipment.Positions];
        for (int i = 0; i < ids.Length; i++)
        {
            int item = worn.ItemAt((EquipPosition)i);
            ids[i] = item >= 0 && item < data.Items.Count ? data.Items[item].Id : string.Empty;
        }

        return ids;
    }

    /// <summary>Equipment from saved ids; unknown ids leave the position empty.</summary>
    public static Equipment Import(IDataRegistry data, IReadOnlyList<string>? ids)
    {
        ArgumentNullException.ThrowIfNull(data);
        Equipment worn = default;
        for (int i = 0; ids is not null && i < Math.Min(ids.Count, Equipment.Positions); i++)
        {
            if (data.Items.Any(item => item.Id == ids[i]))
            {
                worn = worn.With((EquipPosition)i, data.ItemIndex(ids[i]));
            }
        }

        return worn;
    }
}

/// <summary>A player making something (crafting spec §6): the recipe and when it began. No padding (hashed raw).</summary>
/// <param name="Since">Tick it began.</param>
/// <param name="Recipe">Index in the recipes data.</param>
/// <param name="Reserved">Padding, always 0.</param>
public record struct Crafting(ulong Since, int Recipe, int Reserved);

/// <summary>The item components (equipment, crafting): registration and the state hash.</summary>
public static class ItemComponents
{
    /// <summary>Registers the components with a world.</summary>
    public static void Register(EcsWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        world.Register<Equipment>();
        world.Register<Crafting>();
        world.Register<Fighters>();
    }

    /// <summary>Adds the components, with the entities that hold them.</summary>
    public static void AddTo(ref StateHash hash, EcsWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        hash.Add<Entity>(world.Store<Equipment>().Entities);
        hash.Add<Equipment>(world.Store<Equipment>().Components);
        hash.Add<Entity>(world.Store<Crafting>().Entities);
        hash.Add<Crafting>(world.Store<Crafting>().Components);
        hash.Add<Entity>(world.Store<Fighters>().Entities);
        hash.Add<Fighters>(world.Store<Fighters>().Components);
    }
}

/// <summary>Players' combat stats from their base, level and gear, recomputed from scratch so nothing adds up twice.</summary>
public static class PlayerStats
{
    /// <summary>Sets the avatar's stats from the player base, its level and what it wears, keeping its health share.</summary>
    public static void Restat(CreatureWorld world, Entity avatar)
    {
        ArgumentNullException.ThrowIfNull(world);
        CombatStatsDefinition baseStats = world.Data.Combat.Player;
        ref Combatant combatant = ref world.World.GetComponent<Combatant>(avatar);
        combatant.Guard = baseStats.Guard;
        combatant.ResistPhysical = baseStats.Resist.Physical;
        combatant.ResistPrimal = baseStats.Resist.Primal;
        combatant.MaxStamina = baseStats.Stamina;
        PlayerGrowth.Restat(world.Growth, world.World.GetComponent<PlayerProgress>(avatar), ref combatant, baseStats);
        if (world.World.Has<Equipment>(avatar))
        {
            EquipRules.Apply(world.Data, world.World.GetComponent<Equipment>(avatar), ref combatant);
        }

        combatant.Stamina = Math.Min(combatant.Stamina, combatant.MaxStamina);
    }

    /// <summary>Adds experience; on a level up the stats follow (with gear). True when the level went up.</summary>
    public static bool Earn(CreatureWorld world, Entity avatar, int xp)
    {
        ArgumentNullException.ThrowIfNull(world);
        ref PlayerProgress progress = ref world.World.GetComponent<PlayerProgress>(avatar);
        int before = progress.Level;
        Combatant scratch = world.World.GetComponent<Combatant>(avatar);
        PlayerGrowth.Earn(world.Growth, ref progress, ref scratch, world.Data.Combat.Player, xp);
        if (progress.Level == before)
        {
            return false;
        }

        Restat(world, avatar);
        return true;
    }
}
