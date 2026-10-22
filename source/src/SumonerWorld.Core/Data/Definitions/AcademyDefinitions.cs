namespace SumonerWorld.Core.Data.Definitions;

/// <summary>The Summoner Academy (Academy spec §1, §3, M2-21), from <c>data/academy/academy.json</c>.</summary>
public sealed record AcademyDefinition
{
    /// <summary>Questions the Headmaster asks.</summary>
    public const int QuestionCount = 8;

    /// <summary>Answers per question.</summary>
    public const int AnswerCount = 4;

    /// <summary>Class ids, in <c>SummonerClass</c> order.</summary>
    public static readonly IReadOnlyList<string> ClassIds = ["elf", "human", "necromancer", "dwarf"];

    /// <summary>The campus centre, x in metres.</summary>
    public float X { get; init; }

    /// <summary>The campus centre, z in metres.</summary>
    public float Z { get; init; }

    /// <summary>The campus radius in metres (its wall).</summary>
    public float Radius { get; init; } = 20;

    /// <summary>The Academy's people.</summary>
    public IReadOnlyList<NpcDefinition> Npcs { get; init; } = [];

    /// <summary>The ranking questions, in the order they are asked.</summary>
    public IReadOnlyList<RankingQuestionDefinition> Questions { get; init; } = [];

    /// <summary>Rank score each answer point is worth (answers give 0–3 points).</summary>
    public int ScorePerAnswerPoint { get; init; } = 3;

    /// <summary>The seeded random part of the rank score: −this…+this.</summary>
    public int RandomRange { get; init; } = 15;

    /// <summary>Lowest rank scores of ranks E, D and C (below the first is F).</summary>
    public IReadOnlyList<int> RankThresholds { get; init; } = [30, 50, 70];

    /// <summary>Classes students can join now (M2: <c>elf</c> only).</summary>
    public IReadOnlyList<string> OpenClasses { get; init; } = ["elf"];

    /// <summary>The Headmaster's line for each rank F, E, D, C at the ceremony.</summary>
    public IReadOnlyList<string> RankLines { get; init; } = [];

    /// <summary>The id of the quest whose answers and ceremony this is.</summary>
    public string RankingQuest { get; init; } = "the_ranking";
}

/// <summary>One of the Academy's people (Academy spec §1): they stand, talk and give quests.</summary>
public sealed record NpcDefinition
{
    /// <summary>Stable id in snake_case.</summary>
    public required string Id { get; init; }

    /// <summary>Name (until localization).</summary>
    public required string Name { get; init; }

    /// <summary>What they do: <c>headmaster</c>, <c>arena_master</c>, <c>keeper</c> or <c>vendor</c>.</summary>
    public string Role { get; init; } = "keeper";

    /// <summary>Where they stand, x in metres.</summary>
    public float X { get; init; }

    /// <summary>Where they stand, z in metres.</summary>
    public float Z { get; init; }

    /// <summary>Which way they face, in degrees (0 = +z).</summary>
    public float Facing { get; init; }

    /// <summary>Robe colour, <c>#rrggbb</c>.</summary>
    public string Robe { get; init; } = "#5a4a8a";

    /// <summary>What they say when there is nothing else to say.</summary>
    public string Greeting { get; init; } = string.Empty;
}

/// <summary>One ranking question (Academy spec §3.1).</summary>
public sealed record RankingQuestionDefinition
{
    /// <summary>The question.</summary>
    public required string Text { get; init; }

    /// <summary>Exactly four answers.</summary>
    public IReadOnlyList<RankingAnswerDefinition> Answers { get; init; } = [];
}

/// <summary>One answer: its text, the class points it gives and its rank points (never shown).</summary>
public sealed record RankingAnswerDefinition
{
    /// <summary>The answer.</summary>
    public required string Text { get; init; }

    /// <summary>Points per class id (<c>elf</c>, <c>human</c>, <c>necromancer</c>, <c>dwarf</c>).</summary>
    public IReadOnlyDictionary<string, int> Classes { get; init; } = new Dictionary<string, int>();

    /// <summary>Rank points, 0–3.</summary>
    public int Rank { get; init; }
}
