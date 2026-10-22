using SumonerWorld.Core.Academy;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Progression;
using SumonerWorld.Core.Quests;
using SumonerWorld.Net.Messages;

namespace SumonerWorld.Net.Client;

/// <summary>What a dialogue page asks of the player.</summary>
public enum DialogueKind
{
    /// <summary>Nothing: words only.</summary>
    Words,

    /// <summary>A ranking question: pick one of the answers.</summary>
    Question,

    /// <summary>The ceremony is ready: one option, "Hear my rank".</summary>
    Ceremony,
}

/// <summary>One page of a dialogue: who speaks, what they say and the options.</summary>
/// <param name="Speaker">The person's name.</param>
/// <param name="Text">What they say.</param>
/// <param name="Kind">What it asks.</param>
/// <param name="Question">For a question: its index.</param>
/// <param name="Options">Answers, or the one ceremony option.</param>
public sealed record DialoguePage(string Speaker, string Text, DialogueKind Kind, int Question, IReadOnlyList<string> Options);

/// <summary>
/// What the Academy's people say (Academy spec §3, §6, M2-21), from the data and the player's ranking quest. Plain words; there are
/// no wrong answers and the text never says what an answer gives.
/// </summary>
public static class AcademyText
{
    /// <summary>The option that starts the ceremony.</summary>
    public const string HearMyRank = "Hear my rank";

    /// <summary>The page a person shows now.</summary>
    public static DialoguePage PageOf(AcademyDefinition academy, int npc, QuestEntry? ranking, RosterEntry? profile)
    {
        ArgumentNullException.ThrowIfNull(academy);
        NpcDefinition person = academy.Npcs[npc];
        if (person.Role != "headmaster" || ranking is not QuestEntry quest)
        {
            return Words(person, person.Greeting);
        }

        if (quest.Status != (byte)QuestStatus.Active)
        {
            int rank = profile?.Rank ?? 0;
            return Words(person, $"{RankLine(academy, rank)} Keeper Lisel will soon have someone for you to meet.");
        }

        return quest.Step switch
        {
            0 => Words(person, person.Greeting),
            1 when quest[0] < academy.Questions.Count => new DialoguePage(
                person.Name,
                $"Question {quest[0] + 1} of {academy.Questions.Count}. {academy.Questions[quest[0]].Text}",
                DialogueKind.Question,
                quest[0],
                [.. academy.Questions[quest[0]].Answers.Select(answer => answer.Text)]),
            1 => Words(person, "Hmm, let me think about your answers..."),
            _ => new DialoguePage(person.Name, "Thank you. You answered every question. Now, let me tell you who you are.", DialogueKind.Ceremony, 0, [HearMyRank]),
        };
    }

    /// <summary>The Headmaster's words at the ceremony, from the talk's result.</summary>
    public static string Ceremony(AcademyDefinition academy, byte detail)
    {
        ArgumentNullException.ThrowIfNull(academy);
        Rank rank = TalkResult.RankOf(detail);
        SummonerClass leaning = TalkResult.LeaningOf(detail);
        SummonerClass joined = RankingRules.Joined(academy, leaning);
        string place = leaning == joined
            ? $"You are {Article(joined)} {joined} summoner, now and always."
            : $"Your answers sound like {Article(leaning)} {leaning} summoner, but the Elves' grove calls to you for now. You are an Elf summoner, now and always.";
        return $"{place} {RankLine(academy, (int)rank)}";
    }

    /// <summary>The Headmaster's line for a rank.</summary>
    public static string RankLine(AcademyDefinition academy, int rank)
    {
        ArgumentNullException.ThrowIfNull(academy);
        return academy.RankLines.Count == 0 ? $"Rank {(Rank)rank}." : academy.RankLines[Math.Clamp(rank, 0, academy.RankLines.Count - 1)];
    }

    /// <summary>The prompt shown near a person.</summary>
    public static string Prompt(NpcDefinition npc)
    {
        ArgumentNullException.ThrowIfNull(npc);
        return $"[E] talk to {npc.Name}";
    }

    private static DialoguePage Words(NpcDefinition person, string text) => new(person.Name, text, DialogueKind.Words, 0, []);

    private static string Article(SummonerClass value) => value is SummonerClass.Elf ? "an" : "a";
}
