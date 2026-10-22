using System.Numerics;
using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Data;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Items;
using SumonerWorld.Core.Progression;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.World;
using SumonerWorld.Scenarios;

namespace SumonerWorld.Tests.Core.World;

public sealed class SliceRegionTests : IDisposable
{
    private readonly CreatureWorld _world = new(ShippedData.Current, new CreatureWorldSettings(), seed: 67);
    private readonly SimulationScheduler _scheduler;
    private readonly CreatureSystems _systems;

    public SliceRegionTests()
    {
        _scheduler = new SimulationScheduler(_world.Clock, _world.Events);
        _systems = _world.CreateSystems();
        _systems.RegisterAll(_scheduler);
    }

    public void Dispose() => _world.Dispose();

    [Fact]
    public void The_slice_path_has_its_four_places_in_order_around_the_spawn()
    {
        RegionDefinition region = _world.Data.Region!;
        Assert.Equal(["academy", "memory_grove", "gathering_meadow", "bogfox_den"], region.Places.Select(place => place.Id));
        Assert.Equal("den", SpawnerSystem.PlaceOf(_world.Data, "bogfox_den")!.Look);
    }

    [Fact]
    public void Spawners_fill_their_places_at_once_with_the_right_levels()
    {
        Ticks(151);

        Assert.Equal(6, Count(9001, "mossback"));
        Assert.Equal(4, Count(9004, "bogfox"));
        Assert.Equal(1, Count(9005, "bogfox"));
        Entity leader = Members(9005).Single();
        Assert.Equal(6, _world.World.GetComponent<CreatureGrowth>(leader).Level);
        Assert.All(Members(9004), fox => Assert.InRange(_world.World.GetComponent<CreatureGrowth>(fox).Level, 2, 4));
        PlaceDefinition den = SpawnerSystem.PlaceOf(_world.Data, "bogfox_den")!;
        Assert.All(Members(9004), fox => Assert.InRange(Distance(fox, den), 0, den.Radius));
    }

    [Fact]
    public void A_missing_creature_comes_back_after_the_respawn_time_and_a_pacted_one_no_longer_counts()
    {
        Ticks(151);
        Entity owl = Members(9003).First();
        _world.Services.Bonding.Restore(
            _world.World.GetComponent<MindRef>(owl).Handle, new BondState(OwnershipState.Pacted, 1001, PactTerms.Of(0), 0, 1, false, 0, 0, false));
        Assert.Equal(1, Count(9003, "hollow_owl"));

        Ticks(300 * 30);
        Assert.Equal(1, Count(9003, "hollow_owl"));
        Ticks(450);
        Assert.Equal(2, Count(9003, "hollow_owl"));
    }

    [Fact]
    public void Gathering_spots_are_placed_in_their_places()
    {
        var economy = new Economy();
        GatherSpots.Place(economy, _world.Data, null);

        PlaceDefinition meadow = SpawnerSystem.PlaceOf(_world.Data, "gathering_meadow")!;
        Assert.Equal(13, economy.Nodes.Count);
        Assert.Equal(11, economy.Nodes.Count(node => Vector2.Distance(new Vector2(node.Position.X, node.Position.Z), new Vector2(meadow.X, meadow.Z)) <= meadow.Radius));
    }

    [Fact]
    public void A_region_naming_unknown_places_species_or_nodes_is_refused()
    {
        List<DataFile> files = [.. ShippedData.Files];
        int index = files.FindIndex(file => file.Path.EndsWith("slice_region.json", StringComparison.Ordinal));
        files[index] = files[index] with
        {
            Content = files[index].Content
                .Replace("\"species\": \"hollow_owl\"", "\"species\": \"dragon\"", StringComparison.Ordinal)
                .Replace("\"node\": \"sweetroot_patch\"", "\"node\": \"gold_vein\"", StringComparison.Ordinal),
        };

        var error = Assert.Throws<DataValidationException>(() => DataRegistryLoader.Load(files));

        Assert.Contains(error.Errors, message => message.Contains("'dragon'", StringComparison.Ordinal));
        Assert.Contains(error.Errors, message => message.Contains("'gold_vein'", StringComparison.Ordinal));
    }

    private int Count(int group, string species) =>
        Members(group).Count(creature => _world.Data.Species[_world.World.GetComponent<SpeciesRef>(creature).Index].Id == species
            && _world.Services.Bonding.GetBond(_world.World.GetComponent<MindRef>(creature).Handle).State == OwnershipState.Wild
            && _world.World.GetComponent<Combatant>(creature).Health > 0);

    private List<Entity> Members(int group)
    {
        ComponentStore<GroupRef> groups = _world.World.Store<GroupRef>();
        var members = new List<Entity>();
        for (int i = 0; i < groups.Entities.Length; i++)
        {
            if (groups.Components[i].Group == group)
            {
                members.Add(groups.Entities[i]);
            }
        }

        return members;
    }

    private float Distance(Entity creature, PlaceDefinition place)
    {
        Vector3 at = _world.World.GetComponent<Position>(creature).Value;
        return Vector2.Distance(new Vector2(at.X, at.Z), new Vector2(place.X, place.Z));
    }

    private void Ticks(int count)
    {
        for (int i = 0; i < count; i++)
        {
            _scheduler.RunTick();
        }
    }
}
