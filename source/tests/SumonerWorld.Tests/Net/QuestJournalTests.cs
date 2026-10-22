using SumonerWorld.Net.Client;
using SumonerWorld.Net.Messages;
using SumonerWorld.Scenarios;

namespace SumonerWorld.Tests.Net;

public class QuestJournalTests
{
    private static int Ranking => Q("the_ranking");

    private static int Berries => Q("berries_for_the_road");

    private static int Friend => Q("a_friend_in_the_clearing");

    [Fact]
    public void Quests_are_offered_by_level_and_earlier_quests()
    {
        QuestEntry ranked = new(Ranking, 3, 0);
        Assert.Equal([Ranking], QuestJournal.Offered(ShippedData.Current, [], level: 1));
        Assert.Equal([Q("a_first_friend"), Berries, Friend], QuestJournal.Offered(ShippedData.Current, [ranked], level: 1));
        Assert.Contains(Q("bogfox_trouble"), QuestJournal.Offered(ShippedData.Current, [ranked], level: 2));
        Assert.Contains(Q("a_staff_of_your_own"), QuestJournal.Offered(ShippedData.Current, [ranked, new QuestEntry(Berries, 3, 0)], level: 2));
    }

    [Fact]
    public void Objectives_show_their_progress_and_changes_are_told()
    {
        Assert.Equal(["Gather sweet berries 3/5"], QuestJournal.Objectives(ShippedData.Current, new QuestEntry(Berries, 1, 0, 3)));
        Assert.Equal(["Feed any creature 1/1", "Make a Pact with any creature 0/1"], QuestJournal.Objectives(ShippedData.Current, new QuestEntry(Friend, 1, 1, 1)));

        IReadOnlyList<string> lines = QuestJournal.Changes(
            ShippedData.Current, [new QuestEntry(Berries, 1, 0, 4), new QuestEntry(Friend, 1, 0)], [new QuestEntry(Berries, 3, 0), new QuestEntry(Friend, 1, 1)]);

        Assert.Equal(
            ["Quest done: Berries for the Road! You get 30 XP, 5 gold, Berry meal x1.", "A Friend in the Clearing: Feed a creature, then offer it a Pact."],
            lines);
    }

    private static int Q(string id) => ShippedData.Current.Quests.Select((quest, i) => (quest, i)).Single(item => item.quest.Id == id).i;
}
