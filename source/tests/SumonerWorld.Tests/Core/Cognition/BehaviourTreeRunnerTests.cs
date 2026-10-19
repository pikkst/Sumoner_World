using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Cognition.Behaviour;
using SumonerWorld.Core.Cognition.Decision;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Cognition.Opinions;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Events;
using static SumonerWorld.Tests.Core.Cognition.CognitionTestData;

namespace SumonerWorld.Tests.Core.Cognition;

[Collection(AllocationMeasurements.Name)]
public class BehaviourTreeRunnerTests
{
    private const ushort GoalA = 1;
    private const ushort GoalB = 2;
    private static readonly Entity Actor = new(5, 0);

    [Fact]
    public void A_sequence_emits_intents_in_order_and_waits_for_movement_to_arrive()
    {
        var bt = new Bt(Seq(ShowEmote(Emote.Curiosity), Move(MoveTargetMode.Wander, timeout: 100), Interact(InteractVerb.Eat)));

        Assert.Equal(TreeStatus.Running, bt.Tick(GoalA, 0));
        Assert.Equal(["Emote:Curiosity", "MoveTo:Wander"], bt.Intents);

        Assert.Equal(TreeStatus.Running, bt.Tick(GoalA, 1));
        Assert.Empty(bt.Intents);

        bt.Runner.ReportOutcome(bt.Mind, succeeded: true);
        Assert.Equal(TreeStatus.Success, bt.Tick(GoalA, 2));
        Assert.Equal(["Interact:Eat"], bt.Intents);
    }

    [Fact]
    public void Movement_that_never_arrives_times_out_and_the_tree_starts_again()
    {
        var bt = new Bt(Seq(Move(MoveTargetMode.Wander, timeout: 10), Interact(InteractVerb.Eat)));

        bt.Tick(GoalA, 0);
        Assert.Equal(TreeStatus.Running, bt.Tick(GoalA, 9));
        Assert.Equal(TreeStatus.Failure, bt.Tick(GoalA, 10));
        Assert.Empty(bt.Intents);

        bt.Tick(GoalA, 11);
        Assert.Equal(["MoveTo:Wander"], bt.Intents);
    }

    [Fact]
    public void A_selector_falls_through_when_a_condition_fails()
    {
        var hungryOnly = Seq(Condition(ConsiderationInput.NeedHunger, ConditionOperator.Gte, 500), Interact(InteractVerb.Eat));
        var bt = new Bt(Sel(hungryOnly, Interact(InteractVerb.Rest)));

        bt.Tick(GoalA, 0);
        Assert.Equal(["Interact:Rest"], bt.Intents);

        bt.Tick(GoalA, TicksPerDay);
        Assert.Equal(["Interact:Eat"], bt.Intents);
    }

    [Fact]
    public void Wait_runs_for_its_ticks_then_succeeds()
    {
        var bt = new Bt(Seq(Wait(30), Interact(InteractVerb.Rest)));

        Assert.Equal(TreeStatus.Running, bt.Tick(GoalA, 100));
        Assert.Equal(TreeStatus.Running, bt.Tick(GoalA, 129));
        Assert.Equal(TreeStatus.Success, bt.Tick(GoalA, 130));
        Assert.Equal(["Interact:Rest"], bt.Intents);
    }

    [Fact]
    public void Cooldown_blocks_its_child_until_the_time_has_passed()
    {
        var bt = new Bt(Sel(Cooldown(100, Interact(InteractVerb.Eat)), Interact(InteractVerb.Rest)));

        bt.Tick(GoalA, 0);
        Assert.Equal(["Interact:Eat"], bt.Intents);
        bt.Tick(GoalA, 50);
        Assert.Equal(["Interact:Rest"], bt.Intents);
        bt.Tick(GoalA, 100);
        Assert.Equal(["Interact:Eat"], bt.Intents);
    }

    [Fact]
    public void Repeat_runs_its_child_the_given_number_of_times()
    {
        var bt = new Bt(Repeat(3, Interact(InteractVerb.Socialize)));

        Assert.Equal(TreeStatus.Running, bt.Tick(GoalA, 0));
        Assert.Equal(TreeStatus.Running, bt.Tick(GoalA, 1));
        Assert.Equal(TreeStatus.Success, bt.Tick(GoalA, 2));
    }

    [Theory]
    [InlineData(ParallelPolicy.RequireAll, TreeStatus.Running, TreeStatus.Success)]
    [InlineData(ParallelPolicy.RequireOne, TreeStatus.Success, TreeStatus.Success)]
    public void Parallel_finishes_by_its_policy(ParallelPolicy policy, TreeStatus first, TreeStatus later)
    {
        var bt = new Bt(new BehaviourNodeDefinition
        {
            Type = BehaviourNodeType.Parallel,
            Policy = policy,
            Children = [ShowEmote(Emote.Trust), Wait(5)],
        });

        Assert.Equal(first, bt.Tick(GoalA, 0));
        Assert.Equal(later, bt.Tick(GoalA, 5));
    }

    [Fact]
    public void Changing_the_goal_aborts_the_running_tree_with_a_stop_intent()
    {
        var bt = new Bt(
            Seq(Move(MoveTargetMode.Wander, timeout: 1000), Interact(InteractVerb.Eat)),
            Seq(ShowEmote(Emote.Fear), Move(MoveTargetMode.Wander, timeout: 100)));

        bt.Tick(GoalA, 0);
        bt.Tick(GoalB, 1);

        Assert.Equal(["Stop:Wander", "Emote:Fear", "MoveTo:Wander"], bt.Intents);
    }

    [Fact]
    public void Idle_does_nothing_and_a_new_creature_in_a_reused_slot_starts_fresh()
    {
        var bt = new Bt(Seq(Move(MoveTargetMode.Wander, timeout: 1000), Interact(InteractVerb.Eat)));

        Assert.Equal(TreeStatus.Running, bt.Tick(Decision.Idle, 0));
        Assert.Empty(bt.Intents);

        bt.Tick(GoalA, 1);
        Assert.Equal(["MoveTo:Wander"], bt.Intents);
        bt.Store.Release(bt.Mind);
        MindHandle reused = bt.Store.Allocate(new Personality(0, 0, 0, 0), Needs);

        bt.Tick(GoalA, 2, reused);

        Assert.Equal(reused.Slot, bt.Mind.Slot);
        Assert.Equal(["MoveTo:Wander"], bt.Intents);
    }

    [Fact]
    public void Every_shipped_tree_compiles_and_runs()
    {
        var store = new CreatureMindStore(ShippedRules(), capacity: 2);
        MindHandle mind = store.Allocate(new Personality(0, 0, 0, 0), Needs);
        var events = new EventBus();
        events.Register<CreatureIntent>(16);
        var runner = new BehaviourTreeRunner(store, Sampler(store), events, Registry.Goals, Registry.BehaviourTrees);

        foreach (GoalDefinition goal in Registry.Goals)
        {
            int intents = 0;
            for (ulong tick = 0; tick < 600; tick++)
            {
                runner.Tick(mind, new BehaviourContext(Actor, goal.Id, (goal.Id * 1000UL) + tick, Alice, 300, 1000));
                foreach (CreatureIntent intent in events.Read<CreatureIntent>())
                {
                    intents++;
                    if (intent.Kind == IntentKind.MoveTo)
                    {
                        runner.ReportOutcome(mind, succeeded: true);
                    }
                }

                events.EndTick();
            }

            Assert.True(intents > 0, $"The {goal.Key} tree never did anything.");
        }
    }

    [Fact]
    public void Ticking_does_not_allocate()
    {
        var bt = new Bt(Sel(
            Seq(Condition(ConsiderationInput.NeedHunger, ConditionOperator.Gte, 500), Move(MoveTargetMode.Wander, timeout: 3), Interact(InteractVerb.Eat)),
            Repeat(2, Seq(ShowEmote(Emote.Trust), Wait(2)))));
        bt.Tick(GoalA, 0);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (ulong tick = 1; tick < 5_000; tick++)
        {
            bt.TickSilently(tick % 2000 == 0 ? GoalB : GoalA, tick);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static InputSampler Sampler(CreatureMindStore store)
    {
        MemoryRules rules = ShippedRules();
        var opinions = new OpinionModel(store, rules);
        return new InputSampler(store, new AppraisalModel(store, opinions, rules, AppraisalSettings.Default), opinions, TicksPerDay);
    }

    private static BehaviourNodeDefinition Seq(params BehaviourNodeDefinition[] children) =>
        new() { Type = BehaviourNodeType.Sequence, Children = children };

    private static BehaviourNodeDefinition Sel(params BehaviourNodeDefinition[] children) =>
        new() { Type = BehaviourNodeType.Selector, Children = children };

    private static BehaviourNodeDefinition Condition(ConsiderationInput input, ConditionOperator op, int value) =>
        new() { Type = BehaviourNodeType.Condition, Input = input, Op = op, Value = value };

    private static BehaviourNodeDefinition Move(MoveTargetMode target, int timeout) =>
        new() { Type = BehaviourNodeType.Action, Intent = IntentKind.MoveTo, Target = target, Speed = 100, Timeout = timeout };

    private static BehaviourNodeDefinition ShowEmote(Emote emote) =>
        new() { Type = BehaviourNodeType.Action, Intent = IntentKind.Emote, Emote = emote };

    private static BehaviourNodeDefinition Interact(InteractVerb verb) =>
        new() { Type = BehaviourNodeType.Action, Intent = IntentKind.Interact, Verb = verb };

    private static BehaviourNodeDefinition Wait(int ticks) => new() { Type = BehaviourNodeType.Wait, Ticks = ticks };

    private static BehaviourNodeDefinition Cooldown(int ticks, BehaviourNodeDefinition child) =>
        new() { Type = BehaviourNodeType.Cooldown, Ticks = ticks, Child = child };

    private static BehaviourNodeDefinition Repeat(int times, BehaviourNodeDefinition child) =>
        new() { Type = BehaviourNodeType.Repeat, Times = times, Child = child };

    private sealed class Bt
    {
        private readonly EventBus _events = new();

        public Bt(BehaviourNodeDefinition treeA, BehaviourNodeDefinition? treeB = null)
        {
            Store = new CreatureMindStore(ShippedRules(), capacity: 2);
            Mind = Store.Allocate(new Personality(0, 0, 0, 0), Needs);
            _events.Register<CreatureIntent>(16);
            GoalDefinition[] goals = [GoalDef(GoalA, "Alpha"), GoalDef(GoalB, "Beta")];
            BehaviourTreeDefinition[] trees =
            [
                new() { Goal = "Alpha", Root = treeA },
                new() { Goal = "Beta", Root = treeB ?? Interact(InteractVerb.Rest) },
            ];
            Runner = new BehaviourTreeRunner(Store, Sampler(Store), _events, goals, trees);
        }

        public CreatureMindStore Store { get; }

        public MindHandle Mind { get; }

        public BehaviourTreeRunner Runner { get; }

        public List<string> Intents { get; } = [];

        public TreeStatus Tick(ushort goal, ulong now, MindHandle? mind = null)
        {
            Intents.Clear();
            TreeStatus status = Runner.Tick(mind ?? Mind, new BehaviourContext(Actor, goal, now, null, 0, 1000));
            foreach (CreatureIntent intent in _events.Read<CreatureIntent>())
            {
                Intents.Add(intent.Kind switch
                {
                    IntentKind.Emote => $"Emote:{intent.Emote}",
                    IntentKind.Interact => $"Interact:{intent.Verb}",
                    _ => $"{intent.Kind}:{intent.Target}",
                });
            }

            _events.EndTick();
            return status;
        }

        public void TickSilently(ushort goal, ulong now)
        {
            Runner.Tick(Mind, new BehaviourContext(Actor, goal, now, null, 0, 1000));
            _events.EndTick();
        }

        private static GoalDefinition GoalDef(ushort id, string key) => new()
        {
            Id = id,
            Key = key,
            Weight = 1000,
            Interrupt = false,
            Personality = [],
            Considerations = [new ConsiderationDefinition { Input = ConsiderationInput.Constant, Curve = new CurveDefinition { Type = CurveType.Linear, Slope = 1000 } }],
        };
    }
}
