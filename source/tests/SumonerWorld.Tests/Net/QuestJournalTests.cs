using SumonerWorld.Net.Client;
using SumonerWorld.Net.Messages;
using SumonerWorld.Scenarios;

namespace SumonerWorld.Tests.Net;

public class QuestJournalTests
{
    private static int Ranking => Q("the_ranking");

    private static readonly string[] EarlyStory = ["the_ranking", "a_first_friend", "a_student_no_longer", "empty_eyes"];
    private static readonly string[] Prologues = ["the_silent_herd", "old_colours", "the_lantern", "iron_and_shame"];

    private static int Berries => Q("berries_for_the_road");

    private static int Friend => Q("a_friend_in_the_clearing");

    [Fact]
    public void Quests_are_offered_by_level_and_earlier_quests()
    {
        QuestEntry ranked = new(Ranking, 3, 0);
        QuestEntry first = new(Q("a_first_friend"), 3, 0);
        QuestEntry grove = new(Friend, 3, 0);
        Assert.Equal([Ranking], QuestJournal.Offered(ShippedData.Current, [], level: 1));
        Assert.Equal([Q("a_first_friend")], QuestJournal.Offered(ShippedData.Current, [ranked], level: 1));
        Assert.Equal([Friend, Q("a_student_no_longer")], QuestJournal.Offered(ShippedData.Current, [ranked, first], level: 1));
        Assert.Equal([Berries, Q("a_student_no_longer")], QuestJournal.Offered(ShippedData.Current, [ranked, first, grove], level: 2));
        Assert.Equal([Q("a_staff_of_your_own")], QuestJournal.Offered(ShippedData.Current, [ranked, first, grove, new QuestEntry(Berries, 3, 0), new QuestEntry(Q("a_student_no_longer"), 1, 0)], level: 2));
    }

    [Fact]
    public void Each_class_is_offered_only_its_own_prologue()
    {
        QuestEntry[] done = [.. EarlyStory.Select(id => new QuestEntry(Q(id), 3, 0))];

        for (int summonerClass = 0; summonerClass < Prologues.Length; summonerClass++)
        {
            IReadOnlyList<int> offered = QuestJournal.Offered(ShippedData.Current, done, level: 1, summonerClass);
            Assert.Contains(Q(Prologues[summonerClass]), offered);
            Assert.Equal(1, Prologues.Count(prologue => offered.Contains(Q(prologue))));
        }
    }

    [Fact]
    public void Objectives_show_their_progress_and_changes_are_told()
    {
        Assert.Equal(["Gather sweet berries 3/5"], QuestJournal.Objectives(ShippedData.Current, new QuestEntry(Berries, 1, 1, 3)));
        Assert.Equal(["Feed any creature 1/2"], QuestJournal.Objectives(ShippedData.Current, new QuestEntry(Friend, 1, 1, 1)));

        IReadOnlyList<string> lines = QuestJournal.Changes(
            ShippedData.Current, [new QuestEntry(Berries, 1, 0, 4), new QuestEntry(Friend, 1, 0)], [new QuestEntry(Berries, 3, 0), new QuestEntry(Friend, 1, 1)]);

        Assert.Equal(
            ["Quest done: Berries for the Road! You get 40 XP, 5 gold, Berry meal x1.", "The Memory Grove: Feed two creatures in the grove. Next time, watch how they look at you."],
            lines);
    }

    private static int Q(string id) => ShippedData.Current.Quests.Select((quest, i) => (quest, i)).Single(item => item.quest.Id == id).i;
}
