using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Cognition.Decision;
using SumonerWorld.Core.Cognition.Lod;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Data;
using SumonerWorld.Core.Ecosystem;
using SumonerWorld.Core.Ecs;

namespace SumonerWorld.Core.World;

/// <summary>Saving and loading a <see cref="CreatureWorld"/> (M1-17b).</summary>
public static class WorldSnapshots
{
    /// <summary>Copies everything that is saved. Allocates: call it every few seconds, not every tick.</summary>
    public static WorldSnapshot Capture(CreatureWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        ComponentStore<MindRef> minds = world.World.Store<MindRef>();
        ReadOnlySpan<Entity> entities = minds.Entities;
        var creatures = new List<CreatureSave>(entities.Length);
        for (int i = 0; i < entities.Length; i++)
        {
            creatures.Add(CaptureCreature(world, entities[i]));
        }

        creatures.Sort((a, b) => a.Id.Value.CompareTo(b.Id.Value));
        var reputation = world.Reputation.ExportScores()
            .Select(entry => new ReputationSave(world.Data.Species[entry.Key.Species].Id, entry.Key.Region, entry.Key.Player, entry.Value))
            .ToList();
        return new WorldSnapshot(world.Clock.Current.Index, world.Random.State, world.LastStableId, creatures, reputation);
    }

    /// <summary>Copies one creature as it is now (for the transaction journal).</summary>
    public static CreatureSave CaptureCreature(CreatureWorld world, Entity creature)
    {
        ArgumentNullException.ThrowIfNull(world);
        MindHandle mind = world.World.GetComponent<MindRef>(creature).Handle;
        return new CreatureSave(
            world.World.GetComponent<StableId>(creature),
            world.Data.Species[world.World.GetComponent<SpeciesRef>(creature).Index].Id,
            world.World.GetComponent<Position>(creature).Value,
            world.World.GetComponent<Facing>(creature).Value,
            world.World.GetComponent<GroupRef>(creature).Group,
            world.Minds.Export(mind),
            world.Services.Bonding.GetBond(mind),
            world.World.GetComponent<Progression.CreatureGrowth>(creature),
            world.World.GetComponent<Progression.CreatureSkills>(creature));
    }

    /// <summary>
    /// Rebuilds a saved world. Running it is deterministic from here on; it is a new starting point, not a bit-identical continuation
    /// of the run that saved it (entity and mind slots are reassigned).
    /// </summary>
    public static CreatureWorld Restore(DataRegistry data, CreatureWorldSettings settings, WorldSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        CreatureWorld world = CreatureWorld.Continue(data, settings, snapshot.RandomState, snapshot.Tick, snapshot.LastStableId);
        try
        {
            Fill(world, snapshot);
            return world;
        }
        catch
        {
            world.Dispose();
            throw;
        }
    }

    private static void Fill(CreatureWorld world, WorldSnapshot snapshot)
    {
        foreach (CreatureSave save in snapshot.Creatures)
        {
            ushort species = world.SpeciesIndex(save.Species);
            MindHandle mind = world.Minds.Import(save.Mind);
            Entity creature = world.AddRestoredCreature(save.Id, save.Position, mind);
            world.World.GetComponent<Facing>(creature) = new Facing(save.Facing);
            world.World.Add(creature, new CurrentEmote(Emote.None));
            world.World.Add(creature, new CurrentGoal(Decision.Idle));
            world.World.Add(creature, new AiTier(AiLodTier.Far));
            world.World.Add(creature, default(FocusRef));
            world.World.Add(creature, new GroupRef(save.Group));
            world.World.Add(creature, new SpeciesRef(species));
            world.World.Add(creature, save.Growth ?? Progression.GrowthRules.Migrated(world.Data.Species[species], save.Id.Value, save.Bond.State != Bonding.OwnershipState.Wild));
            world.World.Add(creature, (save.Skills ?? default) with { ReluctantUntil = 0 });
            world.AddCombatant(creature, species);
            world.Services.Bonding.Restore(mind, save.Bond);
        }

        foreach (ReputationSave score in snapshot.Reputation)
        {
            world.Reputation.RestoreScore(new ReputationKey(world.SpeciesIndex(score.Species), score.Region, score.Player), score.Score);
        }
    }
}
