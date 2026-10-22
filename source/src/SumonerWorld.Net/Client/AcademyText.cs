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

    /// <summary>The reading test: pick how the creature feels (M2-21b).</summary>
    Read,
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

    /// <summary>The feelings the reading test offers, in <c>Feeling</c> order.</summary>
    public static readonly IReadOnlyList<string> Feelings = ["Calm", "Scared", "Curious", "Angry", "Happy", "Sad"];

    /// <summary>
    /// The page a person shows now, from the ranking quest's current step (<paramref name="quest"/> is its definition):
    /// the Headmaster asks the questions and holds the ceremony, the Arena Master leads the practical moments (M2-21b).
    /// </summary>
    public static DialoguePage PageOf(AcademyDefinition academy, int npc, QuestEntry? ranking, RosterEntry? profile, QuestDefinition? quest = null)
    {
        ArgumentNullException.ThrowIfNull(academy);
        NpcDefinition person = academy.Npcs[npc];
        if (ranking is not QuestEntry entry || quest is null || person.Role is not ("headmaster" or "arena_master"))
        {
            return Words(person, person.Greeting);
        }

        if (entry.Status != (byte)QuestStatus.Active)
        {
            return person.Role == "headmaster"
                ? Words(person, $"{RankLine(academy, profile?.Rank ?? 0)} Keeper Lisel will soon have someone for you to meet.")
                : Words(person, person.Greeting);
        }

        IReadOnlyList<QuestObjectiveDefinition> objectives = entry.Step < quest.Steps.Count ? quest.Steps[entry.Step].Objectives : [];
        return person.Role == "headmaster" ? Headmaster(academy, person, entry, quest, objectives) : ArenaMaster(person, objectives);
    }

    private static DialoguePage Headmaster(
        AcademyDefinition academy, NpcDefinition person, QuestEntry entry, QuestDefinition quest, IReadOnlyList<QuestObjectiveDefinition> objectives)
    {
        if (objectives.Any(objective => objective.Type == ObjectiveType.Answer))
        {
            int asked = quest.Steps.Take(entry.Step).SelectMany(step => step.Objectives).Where(objective => objective.Type == ObjectiveType.Answer).Sum(objective => objective.Count);
            int index = asked + entry[0];
            return index < academy.Questions.Count
                ? new DialoguePage(
                    person.Name,
                    $"Question {index + 1} of {academy.Questions.Count}. {academy.Questions[index].Text}",
                    DialogueKind.Question,
                    index,
                    [.. academy.Questions[index].Answers.Select(answer => answer.Text)])
                : Words(person, "Hmm, let me think about your answers...");
        }

        bool talkToMe = objectives.Any(objective => objective.Type == ObjectiveType.Talk && objective.Target == person.Id);
        if (talkToMe && entry.Step == quest.Steps.Count - 1)
        {
            return new DialoguePage(person.Name, "Thank you. You answered every question. Now, let me tell you who you are.", DialogueKind.Ceremony, 0, [HearMyRank]);
        }

        return talkToMe || entry.Step == 0 ? Words(person, person.Greeting) : Words(person, "Go and see Arena Master Brann first. He stands right next to me.");
    }

    private static DialoguePage ArenaMaster(NpcDefinition person, IReadOnlyList<QuestObjectiveDefinition> objectives)
    {
        if (objectives.Any(objective => objective.Type == ObjectiveType.Read))
        {
            return new DialoguePage(
                person.Name,
                "A summoner reads creatures. Lock on to a creature with T (or stand next to one) and look at it. How does it feel?",
                DialogueKind.Read,
                0,
                Feelings);
        }

        if (objectives.Any(objective => objective.Type is ObjectiveType.Defeat or ObjectiveType.Offer))
        {
            return Words(
                person,
                "The arena is just inside the gate. Beat the training construct (left click and your spells), then offer it a Pact with P. It will say no: it is not alive. That is fine, it is practice!");
        }

        return objectives.Any(objective => objective.Type == ObjectiveType.Talk && objective.Target == person.Id)
            ? Words(person, "So you are the new student! Fighting is only allowed in the arena. Come, let us practise.")
            : Words(person, "Well done! Go back to the Headmaster.");
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
