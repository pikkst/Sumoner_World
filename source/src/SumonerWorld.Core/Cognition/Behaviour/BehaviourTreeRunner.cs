using SumonerWorld.Core.Cognition.Decision;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Events;

namespace SumonerWorld.Core.Cognition.Behaviour;

/// <summary>
/// <see cref="IBehaviourTreeRunner"/>. Each tree is compiled once into a flat pre-order node array shared by all creatures; each
/// mind slot holds one int and one tick per node, so ticking does not allocate (R21). Composite nodes remember which child is
/// running. Results are deterministic: no randomness, fixed child order (R11).
/// </summary>
public sealed class BehaviourTreeRunner : IBehaviourTreeRunner
{
    private const int StateSlots = BehaviourTreeDefinition.MaxNodes;
    private const byte NoOutcome = 0;
    private const byte Succeeded = 1;
    private const byte Failed = 2;
    private const int ParallelMaskBits = 16;
    private const int ParallelMask = (1 << ParallelMaskBits) - 1;

    private readonly ICreatureMindStore _store;
    private readonly InputSampler _sampler;
    private readonly IEventBus _events;
    private readonly CompiledTree?[] _treesByGoal;
    private readonly int[] _state;
    private readonly ulong[] _ticks;
    private readonly ushort[] _activeGoals;
    private readonly int[] _owners;
    private readonly byte[] _outcomes;
    private readonly int[] _inputs = new int[InputSampler.Count];
    private bool _inputsSampled;

    /// <summary>Compiles the trees and allocates per-creature state for every mind slot of <paramref name="store"/>.</summary>
    public BehaviourTreeRunner(
        ICreatureMindStore store, InputSampler sampler, IEventBus events, IEnumerable<GoalDefinition> goals, IEnumerable<BehaviourTreeDefinition> trees)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(sampler);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(goals);
        ArgumentNullException.ThrowIfNull(trees);

        _store = store;
        _sampler = sampler;
        _events = events;

        GoalDefinition[] goalList = [.. goals];
        _treesByGoal = new CompiledTree?[goalList.Length == 0 ? 1 : goalList.Max(goal => goal.Id) + 1];
        foreach (BehaviourTreeDefinition tree in trees)
        {
            GoalDefinition goal = goalList.FirstOrDefault(candidate => candidate.Key == tree.Goal)
                ?? throw new ArgumentException($"Behaviour tree for unknown goal '{tree.Goal}'.", nameof(trees));
            _treesByGoal[goal.Id] = CompiledTree.Compile(tree);
        }

        _state = new int[store.Capacity * StateSlots];
        _ticks = new ulong[store.Capacity * StateSlots];
        _activeGoals = new ushort[store.Capacity];
        _owners = new int[store.Capacity];
        _outcomes = new byte[store.Capacity];
        Array.Fill(_owners, -1);
    }

    /// <inheritdoc />
    public TreeStatus Tick(MindHandle mind, in BehaviourContext context)
    {
        if (!_store.IsValid(mind))
        {
            throw new InvalidOperationException($"{mind} is not a live creature mind.");
        }

        int slot = mind.Slot;
        if (_owners[slot] != mind.Generation || _activeGoals[slot] != context.Goal)
        {
            bool wasRunning = _owners[slot] == mind.Generation && _activeGoals[slot] != Decision.Decision.Idle;
            ResetSlot(slot);
            _owners[slot] = mind.Generation;
            _activeGoals[slot] = context.Goal;
            if (wasRunning)
            {
                _events.Publish(new CreatureIntent(context.Actor, IntentKind.Stop, default, 0, default, default, context.Focus));
            }
        }

        CompiledTree? tree = context.Goal < _treesByGoal.Length ? _treesByGoal[context.Goal] : null;
        if (tree is null)
        {
            return TreeStatus.Running;
        }

        _inputsSampled = false;
        TreeStatus status = Evaluate(tree, 0, mind, slot * StateSlots, context);
        if (status != TreeStatus.Running)
        {
            ResetProgress(slot);
        }

        return status;
    }

    /// <inheritdoc />
    public void ReportOutcome(MindHandle mind, bool succeeded)
    {
        if (!_store.IsValid(mind))
        {
            throw new InvalidOperationException($"{mind} is not a live creature mind.");
        }

        _outcomes[mind.Slot] = succeeded ? Succeeded : Failed;
    }

    // Progress only: node ticks are kept, so cooldowns survive the tree restarting. Wait and MoveTo read their tick only while
    // their state says they are active, so a stale tick is harmless.
    private void ResetProgress(int slot)
    {
        Array.Clear(_state, slot * StateSlots, StateSlots);
        _outcomes[slot] = NoOutcome;
    }

    // A new goal or a new creature in the slot: forget everything, cooldowns included.
    private void ResetSlot(int slot)
    {
        ResetProgress(slot);
        Array.Clear(_ticks, slot * StateSlots, StateSlots);
    }

    private void ResetSubtree(CompiledTree tree, int node, int stateBase) =>
        Array.Clear(_state, stateBase + node, tree.SubtreeSizes[node]);

    private TreeStatus Evaluate(CompiledTree tree, int node, MindHandle mind, int stateBase, in BehaviourContext context)
    {
        BehaviourNodeDefinition definition = tree.Nodes[node];
        ref int state = ref _state[stateBase + node];
        ref ulong started = ref _ticks[stateBase + node];

        switch (definition.Type)
        {
            case BehaviourNodeType.Sequence:
            case BehaviourNodeType.Selector:
                {
                    TreeStatus stopOn = definition.Type == BehaviourNodeType.Sequence ? TreeStatus.Failure : TreeStatus.Success;
                    int count = tree.ChildCounts[node];
                    for (int i = state; i < count; i++)
                    {
                        TreeStatus status = Evaluate(tree, tree.Children[tree.ChildStarts[node] + i], mind, stateBase, context);
                        if (status == TreeStatus.Running)
                        {
                            state = i;
                            return TreeStatus.Running;
                        }

                        if (status == stopOn)
                        {
                            ResetSubtree(tree, node, stateBase);
                            return stopOn;
                        }
                    }

                    ResetSubtree(tree, node, stateBase);
                    return stopOn == TreeStatus.Failure ? TreeStatus.Success : TreeStatus.Failure;
                }

            case BehaviourNodeType.Parallel:
                return EvaluateParallel(tree, node, mind, stateBase, context, ref state);

            case BehaviourNodeType.Condition:
                return Compare(Input(mind, context, definition.Input!.Value), definition.Op, definition.Value)
                    ? TreeStatus.Success
                    : TreeStatus.Failure;

            case BehaviourNodeType.Wait:
                if (state == 0)
                {
                    state = 1;
                    started = context.Now;
                }

                if (context.Now - started >= (ulong)definition.Ticks)
                {
                    state = 0;
                    return TreeStatus.Success;
                }

                return TreeStatus.Running;

            case BehaviourNodeType.Cooldown:
                {
                    if (context.Now < started)
                    {
                        return TreeStatus.Failure;
                    }

                    int child = tree.Children[tree.ChildStarts[node]];
                    TreeStatus status = Evaluate(tree, child, mind, stateBase, context);
                    if (status != TreeStatus.Running)
                    {
                        started = context.Now + (ulong)definition.Ticks;
                    }

                    return status;
                }

            case BehaviourNodeType.Repeat:
                {
                    int child = tree.Children[tree.ChildStarts[node]];
                    TreeStatus status = Evaluate(tree, child, mind, stateBase, context);
                    if (status == TreeStatus.Failure)
                    {
                        state = 0;
                        return TreeStatus.Failure;
                    }

                    if (status == TreeStatus.Success)
                    {
                        state++;
                        ResetSubtree(tree, child, stateBase);
                        if (definition.Times > 0 && state >= definition.Times)
                        {
                            state = 0;
                            return TreeStatus.Success;
                        }
                    }

                    return TreeStatus.Running;
                }

            case BehaviourNodeType.Action:
                return EvaluateAction(definition, mind, context, ref state, ref started);

            default:
                throw new InvalidOperationException($"Unknown behaviour node type {definition.Type}.");
        }
    }

    // State packs which children have finished: low 16 bits = succeeded, high 16 bits = failed.
    private TreeStatus EvaluateParallel(CompiledTree tree, int node, MindHandle mind, int stateBase, in BehaviourContext context, ref int state)
    {
        BehaviourNodeDefinition definition = tree.Nodes[node];
        int count = tree.ChildCounts[node];
        int succeeded = state & ParallelMask;
        int failed = (state >> ParallelMaskBits) & ParallelMask;
        for (int i = 0; i < count; i++)
        {
            int bit = 1 << i;
            if (((succeeded | failed) & bit) != 0)
            {
                continue;
            }

            TreeStatus status = Evaluate(tree, tree.Children[tree.ChildStarts[node] + i], mind, stateBase, context);
            if (status == TreeStatus.Success)
            {
                succeeded |= bit;
            }
            else if (status == TreeStatus.Failure)
            {
                failed |= bit;
            }
        }

        int all = (1 << count) - 1;
        TreeStatus result =
            definition.Policy == ParallelPolicy.RequireAll
                ? failed != 0 ? TreeStatus.Failure : succeeded == all ? TreeStatus.Success : TreeStatus.Running
                : succeeded != 0 ? TreeStatus.Success : failed == all ? TreeStatus.Failure : TreeStatus.Running;

        if (result == TreeStatus.Running)
        {
            state = succeeded | (failed << ParallelMaskBits);
        }
        else
        {
            ResetSubtree(tree, node, stateBase);
        }

        return result;
    }

    private TreeStatus EvaluateAction(BehaviourNodeDefinition definition, MindHandle mind, in BehaviourContext context, ref int state, ref ulong started)
    {
        IntentKind kind = definition.Intent!.Value;
        if (kind != IntentKind.MoveTo)
        {
            _events.Publish(new CreatureIntent(context.Actor, kind, default, 0, definition.Emote ?? default, definition.Verb, context.Focus));
            return TreeStatus.Success;
        }

        if (state == 0)
        {
            _outcomes[mind.Slot] = NoOutcome;
            _events.Publish(new CreatureIntent(context.Actor, kind, definition.Target, definition.Speed, default, default, context.Focus));
            state = 1;
            started = context.Now;
            return TreeStatus.Running;
        }

        byte outcome = _outcomes[mind.Slot];
        if (outcome != NoOutcome || context.Now - started >= (ulong)definition.Timeout)
        {
            _outcomes[mind.Slot] = NoOutcome;
            state = 0;
            return outcome == Succeeded ? TreeStatus.Success : TreeStatus.Failure;
        }

        return TreeStatus.Running;
    }

    private int Input(MindHandle mind, in BehaviourContext context, ConsiderationInput input)
    {
        if (!_inputsSampled)
        {
            _sampler.Sample(mind, context.Now, context.Focus, context.FocusDistance, context.Health).CopyTo(_inputs);
            _inputsSampled = true;
        }

        return _inputs[(int)input];
    }

    private static bool Compare(int input, ConditionOperator op, int value) => op switch
    {
        ConditionOperator.Lt => input < value,
        ConditionOperator.Lte => input <= value,
        ConditionOperator.Gt => input > value,
        ConditionOperator.Gte => input >= value,
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, "Unknown condition operator."),
    };

    /// <summary>A tree flattened in pre-order: a node's subtree is the contiguous range [node, node + size).</summary>
    private sealed class CompiledTree
    {
        private CompiledTree(BehaviourNodeDefinition[] nodes, int[] subtreeSizes, int[] childStarts, int[] childCounts, int[] children)
        {
            Nodes = nodes;
            SubtreeSizes = subtreeSizes;
            ChildStarts = childStarts;
            ChildCounts = childCounts;
            Children = children;
        }

        public BehaviourNodeDefinition[] Nodes { get; }

        public int[] SubtreeSizes { get; }

        public int[] ChildStarts { get; }

        public int[] ChildCounts { get; }

        public int[] Children { get; }

        public static CompiledTree Compile(BehaviourTreeDefinition tree)
        {
            var nodes = new List<BehaviourNodeDefinition>();
            var sizes = new List<int>();
            var childLists = new List<List<int>>();
            Add(tree.Root, nodes, sizes, childLists);
            if (nodes.Count > BehaviourTreeDefinition.MaxNodes)
            {
                throw new ArgumentException($"Tree for '{tree.Goal}' has {nodes.Count} nodes (max {BehaviourTreeDefinition.MaxNodes}).", nameof(tree));
            }

            var starts = new int[nodes.Count];
            var counts = new int[nodes.Count];
            var children = new List<int>();
            for (int i = 0; i < nodes.Count; i++)
            {
                starts[i] = children.Count;
                counts[i] = childLists[i].Count;
                children.AddRange(childLists[i]);
            }

            return new CompiledTree([.. nodes], [.. sizes], starts, counts, [.. children]);
        }

        private static int Add(BehaviourNodeDefinition node, List<BehaviourNodeDefinition> nodes, List<int> sizes, List<List<int>> childLists)
        {
            int index = nodes.Count;
            nodes.Add(node);
            sizes.Add(0);
            childLists.Add([]);

            IEnumerable<BehaviourNodeDefinition> children = node.Children ?? (node.Child is null ? [] : [node.Child]);
            foreach (BehaviourNodeDefinition child in children)
            {
                childLists[index].Add(Add(child, nodes, sizes, childLists));
            }

            sizes[index] = nodes.Count - index;
            return index;
        }
    }
}
