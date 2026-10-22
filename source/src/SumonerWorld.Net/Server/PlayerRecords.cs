using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Items;
using SumonerWorld.Core.Progression;
using SumonerWorld.Core.World;

namespace SumonerWorld.Net.Server;

/// <summary>A player's saved state: where they are, their progress (M2-09) and their inventory (M2-13).</summary>
internal static class PlayerRecords
{
    /// <summary>The player's state now.</summary>
    public static PlayerSave Capture(CreatureWorld world, ulong account, Entity avatar) =>
        new(
            account,
            world.World.GetComponent<Position>(avatar).Value,
            world.World.GetComponent<PlayerProgress>(avatar),
            world.Economy.Export(account, world.Data),
            EquipRules.Export(world.Data, world.World.GetComponent<Equipment>(avatar)),
            world.Economy.Quests.Export(account, world.Data));

    /// <summary>Loads a saved player's inventory (their avatar takes the rest when it enters).</summary>
    public static void Restore(CreatureWorld world, PlayerSave saved)
    {
        if (saved.Inventory is InventorySave inventory)
        {
            world.Economy.Import(saved.Account, inventory, world.Data);
        }

        world.Economy.Quests.Import(saved.Account, saved.Quests, world.Data);
    }

    /// <summary>A returning player's avatar gets their class, rank and level back; stats follow the level, at full health.</summary>
    public static void Enter(CreatureWorld world, Entity avatar, PlayerSave saved)
    {
        if (saved.Progress is not PlayerProgress progress)
        {
            return;
        }

        world.World.GetComponent<PlayerProgress>(avatar) = progress;
        world.World.GetComponent<Equipment>(avatar) = EquipRules.Import(world.Data, saved.Equipped);
        PlayerStats.Restat(world, avatar);
        ref Combatant combatant = ref world.World.GetComponent<Combatant>(avatar);
        combatant.Health = combatant.MaxHealth;
    }
}
