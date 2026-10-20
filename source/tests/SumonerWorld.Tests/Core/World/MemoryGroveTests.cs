using System.Numerics;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.World;
using SumonerWorld.Scenarios;

namespace SumonerWorld.Tests.Core.World;

public class MemoryGroveTests
{
    private static readonly Vector3 Start = new(256, 0, 256);

    [Fact]
    public void The_grove_has_a_herd_a_pack_a_loner_and_two_snared_creatures_near_the_start()
    {
        using var world = new CreatureWorld(ShippedData.Current, new CreatureWorldSettings(), seed: 0x6E0E);

        IReadOnlyList<Entity> creatures = MemoryGrove.Populate(world, Start);

        Assert.Equal(MemoryGrove.CreatureCount, creatures.Count);
        Dictionary<string, int> bySpecies = creatures.GroupBy(c => world.Data.Species[world.World.GetComponent<SpeciesRef>(c).Index].Id)
            .ToDictionary(group => group.Key, group => group.Count());
        Assert.Equal(new Dictionary<string, int> { ["mossback"] = 9, ["bogfox"] = 4, ["hollow_owl"] = 1 }, bySpecies);
        Assert.Equal(2, creatures.Count(world.World.Has<Snared>));
        Assert.All(creatures, c => Assert.InRange(Vector3.Distance(world.World.GetComponent<Position>(c).Value, Start), 5f, 60f));
        Assert.Equal(3, creatures.Select(c => world.World.GetComponent<GroupRef>(c).Group).Distinct().Count());
    }

    [Fact]
    public void Every_session_starts_with_the_same_grove_and_it_stays_together_for_a_minute()
    {
        using var first = new CreatureWorld(ShippedData.Current, new CreatureWorldSettings(), seed: 0x6E0E);
        using var second = new CreatureWorld(ShippedData.Current, new CreatureWorldSettings(), seed: 0x6E0E);
        IReadOnlyList<Entity> a = MemoryGrove.Populate(first, Start);
        IReadOnlyList<Entity> b = MemoryGrove.Populate(second, Start);
        Assert.Equal(a.Select(c => first.World.GetComponent<Position>(c).Value), b.Select(c => second.World.GetComponent<Position>(c).Value));

        first.SpawnPlayerAvatar(1001, Start);
        var scheduler = new SimulationScheduler(first.Clock, first.Events);
        first.CreateSystems().RegisterAll(scheduler);
        for (int tick = 0; tick < 1_800; tick++)
        {
            scheduler.RunTick();
        }

        Assert.True(a.Count(c => Vector3.Distance(first.World.GetComponent<Position>(c).Value, Start) < 100) >= 10, "Most of the grove is still within 100 m after a minute.");
        Assert.Equal(new Vector3(256, 0, 268), first.World.GetComponent<Position>(a[0]).Value);
    }
}
