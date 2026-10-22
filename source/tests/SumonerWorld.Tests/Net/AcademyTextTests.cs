using SumonerWorld.Core.Academy;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Progression;
using SumonerWorld.Net.Client;
using SumonerWorld.Net.Messages;
using SumonerWorld.Scenarios;

namespace SumonerWorld.Tests.Net;

public class AcademyTextTests
{
    private static AcademyDefinition Academy => ShippedData.Current.Academy!;

    [Fact]
    public void The_headmaster_asks_the_questions_around_the_arena_and_then_offers_the_ceremony()
    {
        int headmaster = RankingRules.NpcIndex(ShippedData.Current, "headmaster");
        int arena = RankingRules.NpcIndex(ShippedData.Current, "arena_master");
        QuestDefinition quest = ShippedData.Current.Quests[RankingRules.RankingQuest(ShippedData.Current)];

        DialoguePage first = AcademyText.PageOf(Academy, headmaster, new QuestEntry(0, 1, 1, 3), null, quest);
        Assert.Equal((DialogueKind.Question, 3, 4), (first.Kind, first.Question, first.Options.Count));
        Assert.StartsWith("Question 4 of 8.", first.Text, StringComparison.Ordinal);
        DialoguePage later = AcademyText.PageOf(Academy, headmaster, new QuestEntry(0, 1, 5, 1), null, quest);
        Assert.Equal((DialogueKind.Question, 5), (later.Kind, later.Question));

        Assert.Equal(DialogueKind.Ceremony, AcademyText.PageOf(Academy, headmaster, new QuestEntry(0, 1, 6), null, quest).Kind);
        Assert.StartsWith("Go and see Arena Master", AcademyText.PageOf(Academy, headmaster, new QuestEntry(0, 1, 3), null, quest).Text, StringComparison.Ordinal);
        Assert.Equal(DialogueKind.Read, AcademyText.PageOf(Academy, arena, new QuestEntry(0, 1, 4), null, quest).Kind);
        Assert.Contains("training construct", AcademyText.PageOf(Academy, arena, new QuestEntry(0, 1, 3), null, quest).Text, StringComparison.Ordinal);
        Assert.StartsWith("Rank D", AcademyText.PageOf(Academy, headmaster, new QuestEntry(0, 3, 0), new RosterEntry(1, 0, 2, 3, 0), quest).Text, StringComparison.Ordinal);
        Assert.Equal(Academy.Npcs[2].Greeting, AcademyText.PageOf(Academy, 2, new QuestEntry(0, 1, 1), null, quest).Text);
    }

    [Fact]
    public void The_ceremony_names_the_class_and_the_rank_and_says_when_another_class_called()
    {
        string elf = AcademyText.Ceremony(Academy, TalkResult.Ceremony(Rank.C, SummonerClass.Elf));
        string human = AcademyText.Ceremony(Academy, TalkResult.Ceremony(Rank.F, SummonerClass.Human));

        Assert.StartsWith("You are an Elf summoner", elf, StringComparison.Ordinal);
        Assert.Contains("Rank C", elf, StringComparison.Ordinal);
        Assert.Contains("sound like a Human summoner, but the Elves' grove calls to you for now", human, StringComparison.Ordinal);
        Assert.Contains("Rank F", human, StringComparison.Ordinal);
    }
}
