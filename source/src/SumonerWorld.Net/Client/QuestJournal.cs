using SumonerWorld.Core.Data;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Quests;
using SumonerWorld.Net.Messages;

namespace SumonerWorld.Net.Client;

/// <summary>
/// What the quest log and tracker show, from the server's quest list (quests spec §5, M2-19): which quests are offered, taken
/// and done, and the lines that say what changed. The server decides; this only reads.
/// </summary>
public static class QuestJournal
{
    /// <summary>The status of a quest in the list (<see cref="QuestStatus.None"/> when it is not there).</summary>
    public static QuestStatus StatusOf(IReadOnlyList<QuestEntry> quests, int quest)
    {
        ArgumentNullException.ThrowIfNull(quests);
        foreach (QuestEntry entry in quests)
        {
            if (entry.Quest == quest)
            {
                return (QuestStatus)entry.Status;
            }
        }

        return QuestStatus.None;
    }

    /// <summary>
    /// Quests offered now: not taken, the level reached, every quest before them done, and for the player's class
    /// (<paramref name="summonerClass"/>; −1 = any; E6).
    /// </summary>
    public static IReadOnlyList<int> Offered(IDataRegistry data, IReadOnlyList<QuestEntry> quests, int level, int summonerClass = -1)
    {
        ArgumentNullException.ThrowIfNull(data);
        var offered = new List<int>();
        for (int i = 0; i < data.Quests.Count; i++)
        {
            QuestDefinition quest = data.Quests[i];
            if (StatusOf(quests, i) == QuestStatus.None && level >= quest.MinLevel && (summonerClass < 0 || Core.Quests.QuestBook.IsFor(quest, summonerClass))
                && quest.Requires.All(required => StatusOf(quests, IndexOf(data, required)) == QuestStatus.Done))
            {
                offered.Add(i);
            }
        }

        return offered;
    }

    /// <summary>The objective lines of a taken quest's current step.</summary>
    public static IReadOnlyList<string> Objectives(IDataRegistry data, QuestEntry entry)
    {
        ArgumentNullException.ThrowIfNull(data);
        QuestDefinition quest = data.Quests[entry.Quest];
        if (entry.Step >= quest.Steps.Count)
        {
            return [];
        }

        IReadOnlyList<QuestObjectiveDefinition> objectives = quest.Steps[entry.Step].Objectives;
        return [.. objectives.Select((objective, i) => InteractionText.ObjectiveLine(objective, entry[i]))];
    }

    /// <summary>
    /// The lines for what changed between two quest lists: a step moved on, or a quest was done (with its rewards). Taking and
    /// leaving quests are said by their intents' results.
    /// </summary>
    public static IReadOnlyList<string> Changes(IDataRegistry data, IReadOnlyList<QuestEntry> before, IReadOnlyList<QuestEntry> after)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var lines = new List<string>();
        foreach (QuestEntry now in after)
        {
            QuestEntry? was = before.Any(entry => entry.Quest == now.Quest) ? before.First(entry => entry.Quest == now.Quest) : null;
            QuestDefinition quest = data.Quests[now.Quest];
            if (now.Status == (byte)QuestStatus.Done && was is QuestEntry earlier && earlier.Status != (byte)QuestStatus.Done)
            {
                lines.Add($"Quest done: {quest.Title}! You get {InteractionText.RewardsLine(quest.Rewards)}.");
            }
            else if (now.Status == (byte)QuestStatus.Active && was is QuestEntry moved && moved.Step != now.Step && now.Step < quest.Steps.Count)
            {
                lines.Add($"{quest.Title}: {quest.Steps[now.Step].Text}");
            }
        }

        return lines;
    }

    private static int IndexOf(IDataRegistry data, string id)
    {
        for (int i = 0; i < data.Quests.Count; i++)
        {
            if (data.Quests[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }
}
