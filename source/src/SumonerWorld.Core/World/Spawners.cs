using System.Numerics;
using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Data;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Items;
using SumonerWorld.Core.Progression;
using SumonerWorld.Core.Simulation;

namespace SumonerWorld.Core.World;

/// <summary>
/// Keeps the slice region's places lived in (M2-16): every spawner keeps its count of wild creatures of its species in its group at
/// its place. On the first check the missing ones arrive at once (a new world, or a saved world from before the place existed);
/// later a missing one comes back after the spawner's respawn time. Creatures that made a Pact no longer count.
/// </summary>
public sealed class SpawnerSystem : ISimulationSystem
{
    private const ulong CheckEveryTicks = 150;

    private readonly CreatureWorld _world;
    private readonly RegionDefinition? _region;
    private readonly ulong[] _missingSince;
    private bool _filled;

    /// <summary>Creates the system for a world (it does nothing without a region in the data).</summary>
    public SpawnerSystem(CreatureWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
        _region = world.Data.Region;
        _missingSince = new ulong[_region?.Spawners.Count ?? 0];
    }

    /// <summary>Creatures spawned since the start.</summary>
    public int Spawned { get; private set; }

    /// <inheritdoc />
    public SimulationStage Stage => SimulationStage.Ecosystem;

    /// <summary>The place with an id, or null.</summary>
    public static PlaceDefinition? PlaceOf(IDataRegistry data, string id)
    {
        ArgumentNullException.ThrowIfNull(data);
        foreach (PlaceDefinition place in data.Region?.Places ?? [])
        {
            if (place.Id == id)
            {
                return place;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public void Execute(in SimulationTick tick)
    {
        if (_region is null || tick.Index % CheckEveryTicks != 0)
        {
            return;
        }

        ulong respawnTicksPerSecond = (ulong)_world.Clock.TicksPerSecond;
        for (int i = 0; i < _region.Spawners.Count; i++)
        {
            SpawnerDefinition spawner = _region.Spawners[i];
            int missing = spawner.Count - Living(spawner);
            if (missing <= 0)
            {
                _missingSince[i] = 0;
                continue;
            }

            if (!_filled)
            {
                for (int n = 0; n < missing; n++)
                {
                    Spawn(spawner);
                }

                continue;
            }

            if (_missingSince[i] == 0)
            {
                _missingSince[i] = tick.Index;
            }
            else if (tick.Index - _missingSince[i] >= (ulong)spawner.RespawnSeconds * respawnTicksPerSecond)
            {
                Spawn(spawner);
                _missingSince[i] = missing > 1 ? tick.Index : 0;
            }
        }

        _filled = true;
    }

    // Wild creatures of the spawner's species and group, alive.
    private int Living(SpawnerDefinition spawner)
    {
        ushort species = _world.SpeciesIndex(spawner.Species);
        ComponentStore<GroupRef> groups = _world.World.Store<GroupRef>();
        ReadOnlySpan<Entity> creatures = groups.Entities;
        int living = 0;
        for (int i = 0; i < creatures.Length; i++)
        {
            Entity creature = creatures[i];
            if (groups.Components[i].Group == spawner.Group && _world.World.GetComponent<SpeciesRef>(creature).Index == species
                && _world.World.GetComponent<Combatant>(creature).Health > 0
                && _world.Services.Bonding.GetBond(_world.World.GetComponent<MindRef>(creature).Handle).State == OwnershipState.Wild)
            {
                living++;
            }
        }

        return living;
    }

    private void Spawn(SpawnerDefinition spawner)
    {
        PlaceDefinition place = PlaceOf(_world.Data, spawner.Place)!;
        ushort species = _world.SpeciesIndex(spawner.Species);
        float angle = (float)(_world.Random.NextDouble() * MathF.Tau);
        float distance = (float)(_world.Random.NextDouble() * place.Radius * 0.6f);
        var at = new Vector3(place.X + (MathF.Sin(angle) * distance), 0, place.Z + (MathF.Cos(angle) * distance));
        at.Y = _world.Settings.Ground?.HeightAt(at.X, at.Z) ?? 0;
        Entity creature = _world.SpawnCreature(species, at, Personality.Roll(_world.Data.Species[species], _world.Random), spawner.Group);
        ref CreatureGrowth growth = ref _world.World.GetComponent<CreatureGrowth>(creature);
        growth.Level = _world.Random.NextInt(spawner.MinLevel, spawner.MaxLevel + 1);
        ProgressionComponents.Restat(_world.Growth, _world.Data, _world.World, creature, _world.Data.Species[species].Combat, ref _world.World.GetComponent<Combatant>(creature));
        ref Combatant combatant = ref _world.World.GetComponent<Combatant>(creature);
        combatant.Health = combatant.MaxHealth;
        Spawned++;
    }
}

/// <summary>Gathering spots of the slice region (M2-16): nodes placed in a ring at their place at every start (nodes are not saved).</summary>
public static class GatherSpots
{
    /// <summary>Places the region's gathering nodes.</summary>
    public static void Place(Economy economy, IDataRegistry data, Heightfield? ground)
    {
        ArgumentNullException.ThrowIfNull(economy);
        ArgumentNullException.ThrowIfNull(data);
        IReadOnlyList<GatherSpotDefinition> spots = data.Region?.GatherSpots ?? [];
        for (int s = 0; s < spots.Count; s++)
        {
            GatherSpotDefinition spot = spots[s];
            PlaceDefinition place = SpawnerSystem.PlaceOf(data, spot.Place)!;
            int kind = data.GatherNodes.Select((node, index) => (node, index)).First(item => item.node.Id == spot.Node).index;
            for (int n = 0; n < spot.Count; n++)
            {
                float angle = (n * MathF.Tau / spot.Count) + s;
                float radius = place.Radius * (0.35f + (0.15f * (s % 3)));
                var at = new Vector3(place.X + (MathF.Sin(angle) * radius), 0, place.Z + (MathF.Cos(angle) * radius));
                at.Y = ground?.HeightAt(at.X, at.Z) ?? 0;
                economy.AddNode(kind, at);
            }
        }
    }
}
