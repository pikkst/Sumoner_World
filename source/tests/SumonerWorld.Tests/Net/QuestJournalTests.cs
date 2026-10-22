using SumonerWorld.Net.Client;
using SumonerWorld.Net.Messages;
using SumonerWorld.Scenarios;

namespace SumonerWorld.Tests.Net;

public class QuestJournalTests
{
    [Fact]
    public void Quests_are_offered_by_level_and_earlier_quests()
    {
        Assert.Equal([0, 3], QuestJournal.Offered(ShippedData.Current, [], level: 1));
        Assert.Equal([2, 3], QuestJournal.Offered(ShippedData.Current, [new QuestEntry(0, 1, 0)], level: 2));
        Assert.Equal([1, 2, 3], QuestJournal.Offered(ShippedData.Current, [new QuestEntry(0, 3, 0)], level: 2));
    }

    [Fact]
    public void Objectives_show_their_progress_and_changes_are_told()
    {
        Assert.Equal(["Gather sweet berries 3/5"], QuestJournal.Objectives(ShippedData.Current, new QuestEntry(0, 1, 0, 3)));
        Assert.Equal(["Feed any creature 1/1", "Make a Pact with any creature 0/1"], QuestJournal.Objectives(ShippedData.Current, new QuestEntry(3, 1, 1, 1)));

        IReadOnlyList<string> lines = QuestJournal.Changes(
            ShippedData.Current, [new QuestEntry(0, 1, 0, 4), new QuestEntry(3, 1, 0)], [new QuestEntry(0, 3, 0), new QuestEntry(3, 1, 1)]);

        Assert.Equal(
            ["Quest done: Berries for the Road! You get 30 XP, 5 gold, Berry meal x1.", "A Friend in the Clearing: Feed a creature, then offer it a Pact."],
            lines);
    }
}
