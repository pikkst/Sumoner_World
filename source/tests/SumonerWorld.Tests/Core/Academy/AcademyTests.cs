using System.Numerics;
using SumonerWorld.Core.Academy;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Progression;
using SumonerWorld.Core.Quests;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.Transactions;
using SumonerWorld.Core.World;
using SumonerWorld.Scenarios;

namespace SumonerWorld.Tests.Core.Academy;

public sealed class AcademyTests : IDisposable
{
    private const ulong Alice = 1001;
    private static readonly Vector3 AtGate = new(256, 0, 255);

    private readonly CreatureWorld _world = new(ShippedData.Current, new CreatureWorldSettings(), seed: 65);
    private readonly SimulationScheduler _scheduler;
    private readonly TransactionProcessor _processor = new(new IntentDeduplicator());
    private readonly Entity _alice;
    private readonly int _headmaster;
    private uint _sequence;

    public AcademyTests()
    {
        _scheduler = new SimulationScheduler(_world.Clock, _world.Events);
        _world.CreateSystems().RegisterAll(_scheduler);
        _alice = _world.SpawnPlayerAvatar(Alice, AtGate);
        _headmaster = RankingRules.NpcIndex(_world.Data, "headmaster");
    }

    private AcademyDefinition Academy => _world.Data.Academy!;

    private int RankingQuest => RankingRules.RankingQuest(_world.Data);

    public void Dispose() => _world.Dispose();

    [Fact]
    public void The_shipped_academy_has_its_people_and_eight_questions_of_four_answers()
    {
        Assert.Equal(["headmaster", "arena_master", "elf_keeper"], Academy.Npcs.Select(npc => npc.Id));
        Assert.Equal(8, Academy.Questions.Count);
        Assert.All(Academy.Questions, question => Assert.Equal(4, question.Answers.Count));
    }

    [Fact]
    public void A_new_student_is_given_the_ranking_quest_answers_in_order_and_is_ranked_at_the_ceremony()
    {
        Ticks(16);
        Assert.Equal(QuestStatus.Active, Quests.StatusOf(RankingQuest));
        Assert.Equal(IntentRejectReason.WrongState, Answer(0, 0).Reason);

        Assert.True(Talk().IsApplied);
        Ticks(1);
        Assert.Equal(1, Quests.Find(RankingQuest)!.Step);
        Assert.Equal(IntentRejectReason.InvalidArguments, Answer(1, 0).Reason);

        for (int q = 0; q < AcademyDefinition.QuestionCount; q++)
        {
            Assert.True(Answer(q, q % 4).IsApplied);
            Ticks(1);
        }

        Assert.Equal(2, Quests.Find(RankingQuest)!.Step);
        IntentResult ceremony = Talk();
        Ticks(16);

        Assert.Equal(TalkResult.Ranked, ceremony.Detail & TalkResult.Ranked);
        PlayerProgress progress = _world.World.GetComponent<PlayerProgress>(_alice);
        Assert.Equal((int)TalkResult.RankOf(ceremony.Detail), progress.Rank);
        Assert.Equal((int)SummonerClass.Elf, progress.Class);
        Assert.Equal(QuestStatus.Done, Quests.StatusOf(RankingQuest));
        Assert.Equal(0, Talk().Detail);
        Assert.Equal(IntentRejectReason.InvalidArguments, Answer(0, 1).Reason);
    }

    [Fact]
    public void Talking_needs_the_player_close_to_the_person()
    {
        _world.World.GetComponent<Position>(_alice) = new Position(AtGate + new Vector3(0, 0, 10));

        Assert.Equal(IntentRejectReason.OutOfRange, Talk().Reason);
        Assert.Equal(IntentRejectReason.InvalidArguments, Run(new TalkTransaction(_world, _alice, 99)).Reason);
    }

    [Fact]
    public void Answers_score_ranks_by_the_thresholds_and_ties_go_to_the_roll()
    {
        Assert.Equal(Rank.F, RankingRules.RankFor(Academy, 29));
        Assert.Equal(Rank.E, RankingRules.RankFor(Academy, 30));
        Assert.Equal(Rank.D, RankingRules.RankFor(Academy, 69));
        Assert.Equal(Rank.C, RankingRules.RankFor(Academy, 120));

        var standing = new AcademyStanding();
        Array.Fill(standing.Answers, (byte)2); // the second answer every time: human leaning
        Assert.Equal(SummonerClass.Human, RankingRules.Leaning(Academy, standing, 0));
        Assert.Equal(SummonerClass.Elf, RankingRules.Joined(Academy, SummonerClass.Human));
        Assert.Equal(SummonerClass.Elf, RankingRules.Leaning(Academy, new AcademyStanding(), 0));
        Assert.Equal(SummonerClass.Human, RankingRules.Leaning(Academy, new AcademyStanding(), 1));

        int expected = Academy.Questions.Sum(question => question.Answers[1].Rank) * Academy.ScorePerAnswerPoint;
        Assert.Equal(expected - 15, RankingRules.Score(Academy, standing, -15));
    }

    [Fact]
    public void A_standing_is_saved_and_restored()
    {
        AcademyStanding standing = _world.Economy.Academy.Of(Alice);
        standing.Answers[0] = 3;
        standing.Answers[1] = 1;

        AcademySave saved = _world.Economy.Academy.Export(Alice)!;
        _world.Economy.Academy.Import(2002, saved);

        Assert.Equal(saved, _world.Economy.Academy.Export(2002));
        Assert.Equal(2, _world.Economy.Academy.Of(2002).Answered);
        Assert.Null(_world.Economy.Academy.Export(3003));
    }

    private PlayerQuests Quests => _world.Economy.Quests.Of(Alice);

    private IntentResult Talk() => Run(new TalkTransaction(_world, _alice, _headmaster));

    private IntentResult Answer(int question, int answer) => Run(new AnswerTransaction(_world, _alice, _headmaster, question, answer));

    private void Ticks(int count)
    {
        for (int i = 0; i < count; i++)
        {
            _scheduler.RunTick();
        }
    }

    private IntentResult Run<T>(T transaction)
        where T : struct, ITransaction
    {
        return _processor.Execute(new IntentId(2, ++_sequence), ref transaction, 0);
    }
}
