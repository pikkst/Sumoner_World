namespace SumonerWorld.Core.Data.Definitions;

/// <summary>What an objective counts (quests spec §2).</summary>
public enum ObjectiveType
{
    /// <summary>Creatures defeated that the player fought.</summary>
    Defeat,

    /// <summary>Items gathered from nodes.</summary>
    Gather,

    /// <summary>Items made.</summary>
    Craft,

    /// <summary>Pacts a creature accepted.</summary>
    Pact,

    /// <summary>Creatures fed.</summary>
    Feed,

    /// <summary>Creatures freed from snares.</summary>
    Help,

    /// <summary>Standing within a radius of a place.</summary>
    Reach,

    /// <summary>Talking to one of the Academy's people (target = npc id; M2-21).</summary>
    Talk,

    /// <summary>Answering the Headmaster's ranking questions (M2-21).</summary>
    Answer,

    /// <summary>Telling how a creature feels from its body (M2-21b).</summary>
    Read,

    /// <summary>Offering a Pact, whatever the answer (target = species or any; the Pact rehearsal, M2-21b).</summary>
    Offer,

    /// <summary>Creatures beaten until they yield and then left to go free (target = species or any; E6).</summary>
    Spare,
}

/// <summary>A quest (quests spec §1), from <c>data/quests/*.json</c>.</summary>
public sealed record QuestDefinition
{
    /// <summary>Stable id in snake_case.</summary>
    public required string Id { get; init; }

    /// <summary>Title (until localization).</summary>
    public required string Title { get; init; }

    /// <summary>A sentence or two about it.</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>Lowest player level that is offered it.</summary>
    public int MinLevel { get; init; } = 1;

    /// <summary>Quests that must be done first.</summary>
    public IReadOnlyList<string> Requires { get; init; } = [];

    /// <summary>Taken for every player as soon as they are offered it (the ranking quest, M2-21).</summary>
    public bool AutoStart { get; init; }

    /// <summary>The class id the quest is for (a class prologue, E6); empty = every class.</summary>
    public string Class { get; init; } = string.Empty;

    /// <summary>Steps, in order.</summary>
    public IReadOnlyList<QuestStepDefinition> Steps { get; init; } = [];

    /// <summary>What it gives when done.</summary>
    public QuestRewardsDefinition Rewards { get; init; } = new();
}

/// <summary>One step of a quest: a line of text and the objectives that finish it.</summary>
public sealed record QuestStepDefinition
{
    /// <summary>Most objectives in a step.</summary>
    public const int MaxObjectives = 4;

    /// <summary>What the player is asked to do.</summary>
    public required string Text { get; init; }

    /// <summary>1–4 objectives; all must be done.</summary>
    public IReadOnlyList<QuestObjectiveDefinition> Objectives { get; init; } = [];
}

/// <summary>One objective (quests spec §2).</summary>
public sealed record QuestObjectiveDefinition
{
    /// <summary>What it counts.</summary>
    public required ObjectiveType Type { get; init; }

    /// <summary>The species or item id it counts; empty for any (unused for Reach).</summary>
    public string Target { get; init; } = string.Empty;

    /// <summary>How many.</summary>
    public int Count { get; init; } = 1;

    /// <summary>Reach: the place, x in metres.</summary>
    public float X { get; init; }

    /// <summary>Reach: the place, z in metres.</summary>
    public float Z { get; init; }

    /// <summary>Reach: how close, in metres.</summary>
    public float Radius { get; init; } = 6;

    /// <summary>Reach: what the place is called (for the tracker).</summary>
    public string Place { get; init; } = string.Empty;
}

/// <summary>What a quest gives when done (quests spec §4).</summary>
public sealed record QuestRewardsDefinition
{
    /// <summary>Experience.</summary>
    public int Xp { get; init; }

    /// <summary>Gold.</summary>
    public int Gold { get; init; }

    /// <summary>Items.</summary>
    public IReadOnlyList<RecipePart> Items { get; init; } = [];
}
