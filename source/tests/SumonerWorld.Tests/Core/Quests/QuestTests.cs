using System.Numerics;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Items;
using SumonerWorld.Core.Progression;
using SumonerWorld.Core.Quests;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.Transactions;
using SumonerWorld.Core.World;
using SumonerWorld.Scenarios;

namespace SumonerWorld.Tests.Core.Quests;

public sealed class QuestTests : IDisposable
{
    private const ulong Alice = 1001;
    private const ulong Bob = 1002;
    private static readonly Vector3 Start = new(256, 0, 256);

    private readonly CreatureWorld _world = new(ShippedData.Current, new CreatureWorldSettings(), seed: 64);
    private readonly SimulationScheduler _scheduler;
    private readonly TransactionProcessor _processor = new(new IntentDeduplicator());
    private readonly Entity _alice;
    private uint _sequence;

    public QuestTests()
    {
        _scheduler = new SimulationScheduler(_world.Clock, _world.Events);
        _world.CreateSystems().RegisterAll(_scheduler);
        _alice = _world.SpawnPlayerAvatar(Alice, Start);
        _world.SpawnPlayerAvatar(Bob, Start + new Vector3(2, 0, 0));
        Done();
    }

    private static QuestSave Ranked => new("the_ranking", 0, [], Done: true);

    private PlayerQuests Mine => _world.Economy.Quests.Of(Alice);

    public void Dispose() => _world.Dispose();

    [Fact]
    public void Quests_are_offered_by_level_and_by_the_quests_before_them_and_taken_once()
    {
        int berries = Quest("berries_for_the_road");
        int staff = Quest("a_staff_of_your_own");
        int foxes = Quest("bogfox_trouble");
        Done("a_friend_in_the_clearing");

        Assert.Equal(QuestRefusal.NeedsEarlierQuest, Refusal(new AcceptQuestTransaction(_world, _alice, staff)));
        Assert.Equal(QuestRefusal.LevelTooLow, Refusal(new AcceptQuestTransaction(_world, _alice, foxes)));
        Assert.Equal(IntentRejectReason.InvalidArguments, Run(new AcceptQuestTransaction(_world, _alice, 200)).Reason);

        var accept = new AcceptQuestTransaction(_world, _alice, berries);
        IntentResult first = _processor.Execute(new IntentId(3, 1), ref accept, 0);
        IntentResult again = _processor.Execute(new IntentId(3, 1), ref accept, 0);
        Assert.Equal(first, again);
        Assert.Equal(QuestStatus.Active, Mine.StatusOf(berries));
        Assert.Equal(QuestRefusal.AlreadyTaken, Refusal(new AcceptQuestTransaction(_world, _alice, berries)));

        Assert.True(Run(new AbandonQuestTransaction(_world, _alice, berries)).IsApplied);
        Assert.Equal(QuestStatus.None, Mine.StatusOf(berries));
        Assert.Equal(IntentRejectReason.WrongState, Run(new AbandonQuestTransaction(_world, _alice, berries)).Reason);
    }

    [Fact]
    public void Gathering_counts_only_while_the_quest_is_taken_and_the_quest_rewards_once_done()
    {
        int berries = Quest("berries_for_the_road");
        Done("a_friend_in_the_clearing");
        var meadow = new Vector3(306, 0, 300);
        _world.World.GetComponent<Position>(_alice) = new Position(meadow);
        GatherNode bush = _world.Economy.AddNode(Bush().Kind, meadow + new Vector3(1, 0, 1));
        Assert.True(Run(new GatherTransaction(_world, _alice, bush.Id), 0).IsApplied);
        _scheduler.RunTick();
        Assert.True(Run(new AcceptQuestTransaction(_world, _alice, berries)).IsApplied);
        Ticks(16);
        Assert.Equal((1, 0), (Mine.Find(berries)!.Step, Mine.Find(berries)!.Counts[0]));

        int xp = _world.World.GetComponent<PlayerProgress>(_alice).Xp;
        int gathered = 0;
        for (ulong day = 1; gathered < 5; day++)
        {
            IntentResult gather = Run(new GatherTransaction(_world, _alice, bush.Id), day * (ulong)_world.Data.GatherNodes[bush.Kind].RegrowTicks);
            gathered += gather.Detail;
            Ticks(16);
        }

        Assert.Equal(QuestStatus.Done, Mine.StatusOf(berries));
        Assert.Equal(5, _world.Economy.Inventories.Of(Alice).Gold);
        Assert.Equal(1, _world.Economy.Inventories.Of(Alice).CountOf(_world.Data.ItemIndex("berry_meal")));
        Assert.True(_world.World.GetComponent<PlayerProgress>(_alice).Xp > xp);
    }

    [Fact]
    public void Steps_follow_one_another_and_crafting_counts()
    {
        Done("a_friend_in_the_clearing", "berries_for_the_road");
        int staff = Quest("a_staff_of_your_own");
        Assert.True(Run(new AcceptQuestTransaction(_world, _alice, staff)).IsApplied);

        _world.Economy.Quests.Record(_world.Data, Alice, ObjectiveType.Gather, "fallen_branch", 3);
        Assert.Equal((1, 0), (Mine.Find(staff)!.Step, Mine.Find(staff)!.Counts[0]));

        _world.Economy.Inventories.Of(Alice).Add(_world.Data, [new ItemCount(_world.Data.ItemIndex("fallen_branch"), 3)]);
        int recipe = _world.Data.Recipes.Select((r, i) => (r, i)).Single(item => item.r.Output.Item == "wooden_staff").i;
        Assert.True(Run(new CraftTransaction(_world, _alice, recipe), _world.Clock.Current.Index).IsApplied);
        Ticks(_world.Data.Recipes[recipe].Ticks + 20);

        Assert.Equal(QuestStatus.Done, Mine.StatusOf(staff));
    }

    [Fact]
    public void Every_player_who_fought_a_defeated_creature_gets_the_kill()
    {
        int foxes = Quest("bogfox_trouble");
        Done("a_friend_in_the_clearing", "berries_for_the_road", "a_staff_of_your_own");
        _world.World.GetComponent<PlayerProgress>(_alice).Level = 2;
        Assert.True(Run(new AcceptQuestTransaction(_world, _alice, foxes)).IsApplied);
        _world.Economy.Quests.Record(_world.Data, Alice, ObjectiveType.Reach, string.Empty, 1);
        ushort bogfox = _world.SpeciesIndex("bogfox");
        Entity fox = _world.SpawnCreature(bogfox, Start + new Vector3(0, 0, 6), new Personality(0, 0, 0, 0), group: 3);

        _world.Events.Publish(new CombatantDefeated(fox, bogfox, Bob, 0, Start, new Fighters(Bob, Alice, 0, 0)));
        _scheduler.RunTick();

        Assert.Equal(1, Mine.Find(foxes)!.Counts[0]);
    }

    [Fact]
    public void Reaching_the_grove_finishes_a_reach_objective_and_the_next_step_needs_two_feeds()
    {
        int friend = Quest("a_friend_in_the_clearing");
        Assert.True(Run(new AcceptQuestTransaction(_world, _alice, friend)).IsApplied);
        Ticks(30);
        Assert.Equal(0, Mine.Find(friend)!.Step);

        _world.World.GetComponent<Position>(_alice) = new Position(new Vector3(258, 0, 290));
        Ticks(30);
        Assert.Equal(1, Mine.Find(friend)!.Step);

        _world.Economy.Quests.Record(_world.Data, Alice, ObjectiveType.Feed, "mossback", 1);
        Assert.Equal(QuestStatus.Active, Mine.StatusOf(friend));
        _world.Economy.Quests.Record(_world.Data, Alice, ObjectiveType.Feed, "hollow_owl", 1);
        Assert.Equal(QuestStatus.Ready, Mine.StatusOf(friend));
        Ticks(30);
        Assert.Equal(QuestStatus.Done, Mine.StatusOf(friend));
    }

    [Fact]
    public void A_ready_quest_waits_for_room_in_the_bag()
    {
        int berries = Quest("berries_for_the_road");
        Done("a_friend_in_the_clearing");
        Assert.True(Run(new AcceptQuestTransaction(_world, _alice, berries)).IsApplied);
        _world.Economy.Quests.Record(_world.Data, Alice, ObjectiveType.Reach, string.Empty, 1);
        _world.Economy.Inventories.Of(Alice).Add(_world.Data, [new ItemCount(_world.Data.ItemIndex("owl_feather"), 30 * Inventory.Slots)]);

        _world.Economy.Quests.Record(_world.Data, Alice, ObjectiveType.Gather, "sweet_berries", 5);
        Ticks(30);
        Assert.Equal(QuestStatus.Ready, Mine.StatusOf(berries));

        _world.Economy.Inventories.Of(Alice).Remove(_world.Data.ItemIndex("owl_feather"), 30);
        Ticks(30);
        Assert.Equal(QuestStatus.Done, Mine.StatusOf(berries));
    }

    [Fact]
    public void Quests_are_saved_by_id_and_unknown_ones_are_dropped()
    {
        int berries = Quest("berries_for_the_road");
        int friend = Quest("a_friend_in_the_clearing");
        Done("berries_for_the_road");
        Assert.True(Run(new AcceptQuestTransaction(_world, _alice, friend)).IsApplied);
        _world.Economy.Quests.Record(_world.Data, Alice, ObjectiveType.Reach, string.Empty, 1);

        IReadOnlyList<QuestSave> saved = _world.Economy.Quests.Export(Alice, _world.Data);
        _world.Economy.Quests.Import(Bob, [.. saved, new QuestSave("slay_the_dragon", 0, [], false)], _world.Data);

        Assert.Equal(saved, _world.Economy.Quests.Export(Bob, _world.Data));
        Assert.Equal(QuestStatus.Done, _world.Economy.Quests.Of(Bob).StatusOf(berries));
        Assert.Equal(QuestStatus.Active, _world.Economy.Quests.Of(Bob).StatusOf(friend));
    }

    // Marks the ranking, the first creature and the given quests done for Alice (the slice path is a chain).
    private void Done(params string[] quests) =>
        _world.Economy.Quests.Import(
            Alice, [Ranked, new QuestSave("a_first_friend", 0, [], Done: true), .. quests.Select(id => new QuestSave(id, 0, [], Done: true))], _world.Data);

    private int Quest(string id) => _world.Data.Quests.Select((quest, i) => (quest, i)).Single(item => item.quest.Id == id).i;

    private GatherNode Bush()
    {
        int kind = _world.Data.GatherNodes.Select((node, i) => (node, i)).Single(item => item.node.Id == "berry_bush").i;
        return _world.Economy.AddNode(kind, Start + new Vector3(1, 0, 1));
    }

    private void Ticks(int count)
    {
        for (int i = 0; i < count; i++)
        {
            _scheduler.RunTick();
        }
    }

    private QuestRefusal Refusal<T>(T transaction)
        where T : struct, ITransaction
    {
        IntentResult result = Run(transaction);
        Assert.Equal(IntentRejectReason.WrongState, result.Reason);
        return (QuestRefusal)result.Detail;
    }

    private IntentResult Run<T>(T transaction, ulong now = 0)
        where T : struct, ITransaction
    {
        return _processor.Execute(new IntentId(2, ++_sequence), ref transaction, now);
    }
}
