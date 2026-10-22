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

    /// <summary>Classes students can join now (M2: <c>elf</c> only; all four since E3c).</summary>
    public IReadOnlyList<string> OpenClasses { get; init; } = ["elf"];

    /// <summary>
    /// The first creature of each class (E3c): for a class id, the species a vote for each Elf creature brings instead (caring and
    /// steady, bold, curious). A class or vote that is not listed brings the voted species itself.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> FirstCreatures { get; init; } =
        new Dictionary<string, IReadOnlyDictionary<string, string>>();

    /// <summary>The objective target that means "the Keeper of the player's own class" (E3c).</summary>
    public const string OwnKeeper = "keeper";

    /// <summary>The Headmaster's line for each rank F, E, D, C at the ceremony.</summary>
    public IReadOnlyList<string> RankLines { get; init; } = [];

    /// <summary>The id of the quest whose answers and ceremony this is.</summary>
    public string RankingQuest { get; init; } = "the_ranking";

    /// <summary>The id of the quest in which the Keeper brings the first creature (M2-22).</summary>
    public string FirstFriendQuest { get; init; } = "a_first_friend";

    /// <summary>The Grand Arena's centre, x in metres (M2-21b): fighting is allowed only here inside the campus.</summary>
    public float ArenaX { get; init; }

    /// <summary>The arena's centre, z in metres.</summary>
    public float ArenaZ { get; init; }

    /// <summary>The arena's radius in metres.</summary>
    public float ArenaRadius { get; init; } = 6;

    /// <summary>The species of the training construct that stands in the arena.</summary>
    public string ConstructSpecies { get; init; } = "training_construct";

    /// <summary>Rank score a practical moment adds the first time (a reading right at the first try).</summary>
    public int MomentBonus { get; init; } = 5;

    /// <summary>Rank score a reading adds when it took more than one try.</summary>
    public int LateReadBonus { get; init; } = 2;

    /// <summary>The Keeper of <paramref name="summonerClass"/> (E3c), or null.</summary>
    public NpcDefinition? KeeperOf(Progression.SummonerClass summonerClass)
    {
        string id = ClassIds[(int)summonerClass];
        foreach (NpcDefinition npc in Npcs)
        {
            if (npc.Role == "keeper" && npc.Class == id)
            {
                return npc;
            }
        }

        return null;
    }

    /// <summary>The species a <paramref name="summonerClass"/> student gets for a vote for <paramref name="voted"/> (E3c).</summary>
    public string FirstCreatureOf(Progression.SummonerClass summonerClass, string voted) =>
        FirstCreatures.TryGetValue(ClassIds[(int)summonerClass], out IReadOnlyDictionary<string, string>? map) && map.TryGetValue(voted, out string? species)
            ? species
            : voted;

    /// <summary>True when <paramref name="x"/>, <paramref name="z"/> is inside the campus but outside the arena: no fighting there.</summary>
    public bool IsSafe(float x, float z)
    {
        float campus = ((x - X) * (x - X)) + ((z - Z) * (z - Z));
        float arena = ((x - ArenaX) * (x - ArenaX)) + ((z - ArenaZ) * (z - ArenaZ));
        return campus <= Radius * Radius && arena > ArenaRadius * ArenaRadius;
    }
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

    /// <summary>A Keeper's class id (E3c): the young creatures of that class wait with them; empty for others.</summary>
    public string Class { get; init; } = string.Empty;
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

    /// <summary>The species this answer votes for as the first creature (M2-22); empty for none.</summary>
    public string Creature { get; init; } = string.Empty;
}
