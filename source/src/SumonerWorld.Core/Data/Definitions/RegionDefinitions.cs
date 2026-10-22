namespace SumonerWorld.Core.Data.Definitions;

/// <summary>
/// The region's places and what lives and grows there (M2-16), from <c>data/world/slice_region.json</c>; since E4a also its biomes,
/// which shape the ground and how it looks (the Vale of Oaths, 2 km).
/// </summary>
public sealed record RegionDefinition
{
    /// <summary>Areas with their own ground, plants and creatures (E4a).</summary>
    public IReadOnlyList<BiomeDefinition> Biomes { get; init; } = [];

    /// <summary>Named places along the slice path.</summary>
    public IReadOnlyList<PlaceDefinition> Places { get; init; } = [];

    /// <summary>Creatures the world keeps at a place.</summary>
    public IReadOnlyList<SpawnerDefinition> Spawners { get; init; } = [];

    /// <summary>Gathering nodes placed at a place at every start.</summary>
    public IReadOnlyList<GatherSpotDefinition> GatherSpots { get; init; } = [];

    /// <summary>The people of the Vale's towns and outposts (E5a), after the Academy's in <c>IDataRegistry.People</c>.</summary>
    public IReadOnlyList<NpcDefinition> People { get; init; } = [];
}

/// <summary>
/// A biome (E4a): a circle whose ground is raised, lowered, flattened or roughened (fading out over its outer third), with its own
/// ground colour, plant cover and, for a marsh, standing water.
/// </summary>
public sealed record BiomeDefinition
{
    /// <summary>Stable id in snake_case.</summary>
    public required string Id { get; init; }

    /// <summary>Name shown on the map.</summary>
    public required string Name { get; init; }

    /// <summary>Centre, x in metres.</summary>
    public float X { get; init; }

    /// <summary>Centre, z in metres.</summary>
    public float Z { get; init; }

    /// <summary>Radius in metres.</summary>
    public float Radius { get; init; } = 300;

    /// <summary>Metres added to the ground.</summary>
    public float HeightOffset { get; init; }

    /// <summary>Factor on the ground's hills (below 1 flatter, above 1 rougher).</summary>
    public float HeightScale { get; init; } = 1;

    /// <summary>Ground colour (<c>#RRGGBB</c>).</summary>
    public string Ground { get; init; } = "#47693A";

    /// <summary>Plant cover against the usual (0 = bare, 1 = usual, 2 = dense).</summary>
    public float Plants { get; init; } = 1;

    /// <summary>Colour of its trees and bushes (<c>#RRGGBB</c>).</summary>
    public string PlantColour { get; init; } = "#3D6B45";

    /// <summary>Height of standing water, in metres (null = none).</summary>
    public float? Water { get; init; }
}

/// <summary>A named place (a circle on the ground).</summary>
public sealed record PlaceDefinition
{
    /// <summary>Stable id in snake_case.</summary>
    public required string Id { get; init; }

    /// <summary>Name shown when the player enters it (until localization).</summary>
    public required string Name { get; init; }

    /// <summary>Centre, x in metres.</summary>
    public float X { get; init; }

    /// <summary>Centre, z in metres.</summary>
    public float Z { get; init; }

    /// <summary>Radius in metres.</summary>
    public float Radius { get; init; } = 15;

    /// <summary>A line shown under the name.</summary>
    public string Line { get; init; } = string.Empty;

    /// <summary>What the client draws there: <c>none</c>, <c>den</c> (a ring of rocks), <c>town</c>, <c>outpost</c>, <c>ruins</c> or <c>camp</c>.</summary>
    public string Look { get; init; } = "none";

    /// <summary>An outpost's banner colour (<c>#RRGGBB</c>, E4b); empty for the look's own.</summary>
    public string Colour { get; init; } = string.Empty;
}

/// <summary>Creatures of one species the world keeps at a place: missing ones come back after a while.</summary>
public sealed record SpawnerDefinition
{
    /// <summary>The place id.</summary>
    public required string Place { get; init; }

    /// <summary>The species id.</summary>
    public required string Species { get; init; }

    /// <summary>How many it keeps.</summary>
    public int Count { get; init; } = 1;

    /// <summary>Lowest level of a new one.</summary>
    public int MinLevel { get; init; } = 1;

    /// <summary>Highest level of a new one.</summary>
    public int MaxLevel { get; init; } = 1;

    /// <summary>Seconds before a missing one comes back.</summary>
    public int RespawnSeconds { get; init; } = 300;

    /// <summary>The group id its creatures share (a herd or pack); unique per spawner.</summary>
    public int Group { get; init; }
}

/// <summary>Gathering nodes of one kind placed at a place.</summary>
public sealed record GatherSpotDefinition
{
    /// <summary>The place id.</summary>
    public required string Place { get; init; }

    /// <summary>The gathering node kind id.</summary>
    public required string Node { get; init; }

    /// <summary>How many.</summary>
    public int Count { get; init; } = 3;
}
