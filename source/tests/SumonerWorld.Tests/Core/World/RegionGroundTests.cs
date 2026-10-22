using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Progression;
using SumonerWorld.Core.World;
using SumonerWorld.Scenarios;

namespace SumonerWorld.Tests.Core.World;

/// <summary>The 2 km Vale of Oaths (E4a): biomes shape the ground, mountains wall it, and every class has creatures at home.</summary>
public class RegionGroundTests
{
    private static readonly Lazy<(Heightfield Plain, Heightfield Shaped)> Grounds = new(() =>
        (DevRegion.CreateGround(), DevRegion.CreateGround(DevRegion.Chunks, Region)));

    private static RegionDefinition Region => ShippedData.Current.Region!;

    [Fact]
    public void The_region_is_two_kilometres_and_the_academys_vale_keeps_its_ground()
    {
        (Heightfield plain, Heightfield shaped) = Grounds.Value;
        Assert.Equal(2048f, shaped.Size);
        Assert.Equal(plain.HeightAt(256, 256), shaped.HeightAt(256, 256), 3);
        Assert.Equal(plain.HeightAt(338, 352), shaped.HeightAt(338, 352), 3);
    }

    [Fact]
    public void Highlands_rise_the_marsh_lies_flat_and_low_and_mountains_wall_the_edge()
    {
        (Heightfield plain, Heightfield shaped) = Grounds.Value;
        BiomeDefinition highlands = Region.Biomes.Single(biome => biome.Id == "stormreach");
        BiomeDefinition marsh = Region.Biomes.Single(biome => biome.Id == "mournfen");

        Assert.Equal((plain.HeightAt(highlands.X, highlands.Z) * highlands.HeightScale) + highlands.HeightOffset, shaped.HeightAt(highlands.X, highlands.Z), 2);
        Assert.Equal((plain.HeightAt(marsh.X, marsh.Z) * marsh.HeightScale) + marsh.HeightOffset, shaped.HeightAt(marsh.X, marsh.Z), 2);
        Assert.Equal(plain.HeightAt(1024, 0) + RegionGround.RimHeight, shaped.HeightAt(1024, 0), 2);
        Assert.Equal("Mournfen", RegionGround.BiomeAt(Region, 1500, 500)!.Name);
        Assert.Null(RegionGround.BiomeAt(Region, 1990, 60));
    }

    [Fact]
    public void Every_class_has_its_creatures_living_in_the_world()
    {
        var spawned = Region.Spawners.Select(spawner => spawner.Species).ToHashSet(StringComparer.Ordinal);
        foreach (SummonerClass summonerClass in Enum.GetValues<SummonerClass>())
        {
            Assert.Contains(ShippedData.Current.Species, species => species.Class == summonerClass && spawned.Contains(species.Id));
        }

        Assert.All(Region.Places, place => Assert.InRange(place.X, RegionGround.RimWidth, DevRegion.Size - RegionGround.RimWidth));
    }
}
