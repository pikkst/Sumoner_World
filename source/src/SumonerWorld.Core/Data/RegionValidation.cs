using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.World;

namespace SumonerWorld.Core.Data;

/// <summary>Checks the slice region (M2-16): known places, species and node kinds; levels and counts in range; unique groups.</summary>
internal static class RegionValidation
{
    private const int MaxCount = 30;
    private const int MaxRespawnSeconds = 86_400;
    private static readonly string[] Looks = ["none", "den", "town", "outpost", "ruins", "camp"];

    internal static void Validate(
        RegionDefinition region, string where, List<SpeciesDefinition> species, List<(GatherNodeDefinition Entry, string Where)> nodes, List<string> errors)
    {
        var places = new HashSet<string>(StringComparer.Ordinal);
        foreach (PlaceDefinition place in region.Places)
        {
            if (!DataRegistryLoader.IsSnakeCaseId(place.Id) || !places.Add(place.Id) || place.Radius <= 0 || !Looks.Contains(place.Look))
            {
                errors.Add($"{where}: place '{place.Id}' needs a unique snake_case id, a radius above 0 and a look ({string.Join(", ", Looks)}).");
            }

            if (place.Colour.Length > 0 && !IsColour(place.Colour))
            {
                errors.Add($"{where}: place '{place.Id}' colour '{place.Colour}' must be like #A0B0C0.");
            }

            if (place.X < 0 || place.Z < 0 || place.X > World.DevRegion.Size || place.Z > World.DevRegion.Size)
            {
                errors.Add($"{where}: place '{place.Id}' lies outside the region (0-{World.DevRegion.Size} m).");
            }
        }

        ValidateBiomes(region, where, errors);
        ValidatePeople(region, where, errors);
        var groups = new HashSet<int>();
        foreach (SpawnerDefinition spawner in region.Spawners)
        {
            if (!places.Contains(spawner.Place) || !species.Any(definition => definition.Id == spawner.Species) || !groups.Add(spawner.Group))
            {
                errors.Add($"{where}: spawner of '{spawner.Species}' at '{spawner.Place}' needs a known place and species and its own group.");
            }

            DataRegistryLoader.CheckRange(spawner.Count, 1, MaxCount, "spawners.count", where, errors);
            DataRegistryLoader.CheckRange(spawner.MinLevel, 1, Progression.GrowthRules.MaxLevel, "spawners.minLevel", where, errors);
            DataRegistryLoader.CheckRange(spawner.MaxLevel, spawner.MinLevel, Progression.GrowthRules.MaxLevel, "spawners.maxLevel", where, errors);
            DataRegistryLoader.CheckRange(spawner.RespawnSeconds, 1, MaxRespawnSeconds, "spawners.respawnSeconds", where, errors);
        }

        foreach (GatherSpotDefinition spot in region.GatherSpots)
        {
            if (!places.Contains(spot.Place) || !nodes.Any(node => node.Entry.Id == spot.Node))
            {
                errors.Add($"{where}: gather spot '{spot.Node}' at '{spot.Place}' needs a known place and node kind.");
            }

            DataRegistryLoader.CheckRange(spot.Count, 1, MaxCount, "gatherSpots.count", where, errors);
        }
    }

    private static void ValidateBiomes(RegionDefinition region, string where, List<string> errors)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (BiomeDefinition biome in region.Biomes)
        {
            if (!DataRegistryLoader.IsSnakeCaseId(biome.Id) || !ids.Add(biome.Id) || biome.Radius <= 0
                || biome.HeightScale is < 0 or > 4 || biome.Plants is < 0 or > 3 || !IsColour(biome.Ground) || !IsColour(biome.PlantColour))
            {
                errors.Add($"{where}: biome '{biome.Id}' needs a unique snake_case id, a radius above 0, heightScale 0-4, plants 0-3 and colours like #A0B0C0.");
            }
        }
    }

    // The Vale's people (E5a): unique ids, a known role, and a round inside the region.
    private static void ValidatePeople(RegionDefinition region, string where, List<string> errors)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (NpcDefinition person in region.People)
        {
            if (!DataRegistryLoader.IsSnakeCaseId(person.Id) || !ids.Add(person.Id) || !QuestValidation.NpcRoles.Contains(person.Role) || !IsColour(person.Robe))
            {
                errors.Add($"{where}: person '{person.Id}' needs a unique snake_case id, a role ({string.Join(", ", QuestValidation.NpcRoles)}) and a robe colour.");
            }

            foreach (RoutePoint point in person.Route.Append(new RoutePoint { X = person.X, Z = person.Z }))
            {
                if (point.X < RegionGround.RimWidth || point.Z < RegionGround.RimWidth || point.X > World.DevRegion.Size - RegionGround.RimWidth || point.Z > World.DevRegion.Size - RegionGround.RimWidth)
                {
                    errors.Add($"{where}: person '{person.Id}' stands or walks outside the region's walkable part.");
                    break;
                }
            }
        }
    }

    private static bool IsColour(string colour) => colour is { Length: 7 } && colour[0] == '#' && colour[1..].All(Uri.IsHexDigit);
}
