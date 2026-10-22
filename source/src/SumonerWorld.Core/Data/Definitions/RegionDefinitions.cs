namespace SumonerWorld.Core.Data.Definitions;

/// <summary>The slice region's places and what lives and grows there (M2-16), from <c>data/world/slice_region.json</c>.</summary>
public sealed record RegionDefinition
{
    /// <summary>Named places along the slice path.</summary>
    public IReadOnlyList<PlaceDefinition> Places { get; init; } = [];

    /// <summary>Creatures the world keeps at a place.</summary>
    public IReadOnlyList<SpawnerDefinition> Spawners { get; init; } = [];

    /// <summary>Gathering nodes placed at a place at every start.</summary>
    public IReadOnlyList<GatherSpotDefinition> GatherSpots { get; init; } = [];
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

    /// <summary>What the client draws there: <c>none</c> or <c>den</c> (a ring of rocks).</summary>
    public string Look { get; init; } = "none";
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
