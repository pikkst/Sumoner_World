using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Data;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Core.Ecs;

namespace SumonerWorld.Core.Progression;

/// <summary>The progression components (creature growth, player progress): registration, the state hash and set-up.</summary>
public static class ProgressionComponents
{
    /// <summary>Registers the components with a world.</summary>
    public static void Register(EcsWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        world.Register<CreatureGrowth>();
        world.Register<PlayerProgress>();
        world.Register<CreatureSkills>();
        world.Register<CreatureCombos>();
    }

    /// <summary>Adds the components, with the entities that hold them.</summary>
    public static void AddTo(ref StateHash hash, EcsWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        hash.Add<Entity>(world.Store<CreatureGrowth>().Entities);
        hash.Add<CreatureGrowth>(world.Store<CreatureGrowth>().Components);
        hash.Add<Entity>(world.Store<PlayerProgress>().Entities);
        hash.Add<PlayerProgress>(world.Store<PlayerProgress>().Components);
        hash.Add<Entity>(world.Store<CreatureSkills>().Entities);
        hash.Add<CreatureSkills>(world.Store<CreatureSkills>().Components);
        hash.Add<Entity>(world.Store<CreatureCombos>().Entities);
        hash.Add<CreatureCombos>(world.Store<CreatureCombos>().Components);
    }

    /// <summary>Gives a new wild creature its growth (<see cref="GrowthRules.Wild"/>).</summary>
    public static void AddWild(EcsWorld world, Entity creature, SpeciesDefinition species, ulong stableId)
    {
        ArgumentNullException.ThrowIfNull(world);
        world.Add(creature, GrowthRules.Wild(species, stableId));
        world.Add(creature, default(CreatureSkills));
        world.Add(creature, default(CreatureCombos));
        world.Add(creature, default(Items.Equipment));
    }

    /// <summary>Gives a new player avatar its progress (a new player until profiles are saved, M2-20).</summary>
    public static void AddPlayer(EcsWorld world, Entity avatar)
    {
        ArgumentNullException.ThrowIfNull(world);
        world.Add(avatar, PlayerProgress.New);
        world.Add(avatar, default(Items.Equipment));
    }

    /// <summary>Sets a creature's stats from its growth, its learned passive skills and what it wears (from scratch each time).</summary>
    public static void Restat(GrowthRules rules, DataRegistry data, EcsWorld world, Entity creature, CombatStatsDefinition species, ref Combatant combatant)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(world);
        rules.Apply(ref combatant, species, world.GetComponent<CreatureGrowth>(creature));
        combatant.ResistPhysical = species.Resist.Physical;
        combatant.ResistPrimal = species.Resist.Primal;
        if (world.Has<CreatureSkills>(creature))
        {
            SkillRules.ApplyPassives(
                data.SkillTreeOf(world.GetComponent<World.SpeciesRef>(creature).Index), world.GetComponent<CreatureSkills>(creature).Learned, species, ref combatant);
        }

        if (world.Has<Items.Equipment>(creature))
        {
            Items.EquipRules.Apply(data, world.GetComponent<Items.Equipment>(creature), ref combatant);
            combatant.Stamina = Math.Min(combatant.Stamina, combatant.MaxStamina);
        }
    }
}
