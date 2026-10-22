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
    public void The_headmaster_asks_the_next_question_then_offers_the_ceremony()
    {
        int headmaster = RankingRules.NpcIndex(ShippedData.Current, "headmaster");

        DialoguePage question = AcademyText.PageOf(Academy, headmaster, new QuestEntry(0, 1, 1, 3), null);
        Assert.Equal((DialogueKind.Question, 3, 4), (question.Kind, question.Question, question.Options.Count));
        Assert.StartsWith("Question 4 of 8.", question.Text, StringComparison.Ordinal);

        Assert.Equal(DialogueKind.Ceremony, AcademyText.PageOf(Academy, headmaster, new QuestEntry(0, 1, 2), null).Kind);
        Assert.Equal(DialogueKind.Words, AcademyText.PageOf(Academy, headmaster, new QuestEntry(0, 1, 0), null).Kind);
        Assert.StartsWith("Rank D", AcademyText.PageOf(Academy, headmaster, new QuestEntry(0, 3, 0), new RosterEntry(1, 0, 2, 3, 0)).Text, StringComparison.Ordinal);
        Assert.Equal(Academy.Npcs[1].Greeting, AcademyText.PageOf(Academy, 1, new QuestEntry(0, 1, 1), null).Text);
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
