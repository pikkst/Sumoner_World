using SumonerWorld.Core.Data.Definitions;

namespace SumonerWorld.Core.Data;

/// <summary>Checks the slice region (M2-16): known places, species and node kinds; levels and counts in range; unique groups.</summary>
internal static class RegionValidation
{
    private const int MaxCount = 30;
    private const int MaxRespawnSeconds = 86_400;

    internal static void Validate(
        RegionDefinition region, string where, List<SpeciesDefinition> species, List<(GatherNodeDefinition Entry, string Where)> nodes, List<string> errors)
    {
        var places = new HashSet<string>(StringComparer.Ordinal);
        foreach (PlaceDefinition place in region.Places)
        {
            if (!DataRegistryLoader.IsSnakeCaseId(place.Id) || !places.Add(place.Id) || place.Radius <= 0 || place.Look is not ("none" or "den"))
            {
                errors.Add($"{where}: place '{place.Id}' needs a unique snake_case id, a radius above 0 and a look (none, den).");
            }
        }

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
}
