using SumonerWorld.Core.Cognition.Appraisal;

namespace SumonerWorld.Core.Data.Definitions;

/// <summary>Behaviour tree node kinds (AI Bible §7.3).</summary>
public enum BehaviourNodeType
{
    /// <summary>Runs children in order; fails as soon as one fails.</summary>
    Sequence,

    /// <summary>Runs children in order; succeeds as soon as one succeeds.</summary>
    Selector,

    /// <summary>Runs all children every tick; finishes by <see cref="BehaviourNodeDefinition.Policy"/>.</summary>
    Parallel,

    /// <summary>Compares a consideration input with a value.</summary>
    Condition,

    /// <summary>Emits an intent; MoveTo waits for the simulation's outcome or its timeout.</summary>
    Action,

    /// <summary>Runs for a number of ticks, then succeeds.</summary>
    Wait,

    /// <summary>Runs its child, then fails until the cooldown has passed.</summary>
    Cooldown,

    /// <summary>Runs its child again after each success; <c>times</c> 0 repeats until it fails.</summary>
    Repeat,
}

/// <summary>Comparison used by condition nodes.</summary>
public enum ConditionOperator
{
    /// <summary>Input &lt; value.</summary>
    Lt,

    /// <summary>Input ≤ value.</summary>
    Lte,

    /// <summary>Input &gt; value.</summary>
    Gt,

    /// <summary>Input ≥ value.</summary>
    Gte,
}

/// <summary>When a parallel node finishes.</summary>
public enum ParallelPolicy
{
    /// <summary>Succeeds when every child has succeeded; fails when any child fails.</summary>
    RequireAll,

    /// <summary>Succeeds when any child succeeds; fails when every child has failed.</summary>
    RequireOne,
}

/// <summary>What an action asks the simulation to do (AI Bible §7.3). Intents are validated like player intents (R30).</summary>
public enum IntentKind : byte
{
    /// <summary>Move toward a target (see <see cref="MoveTargetMode"/>); waits for arrival.</summary>
    MoveTo,

    /// <summary>Attack the focus.</summary>
    Attack,

    /// <summary>Show an emote (AI Bible §6.2).</summary>
    Emote,

    /// <summary>Do something in place (eat, rest, socialize, hunt).</summary>
    Interact,

    /// <summary>Cast a spell (arrives with the magic system).</summary>
    Cast,

    /// <summary>Stop moving. Also sent by the runner when a goal change aborts a tree.</summary>
    Stop,
}

/// <summary>Where a MoveTo intent goes.</summary>
public enum MoveTargetMode : byte
{
    /// <summary>A random nearby point (the simulation picks it with the creature's stream).</summary>
    Wander,

    /// <summary>Toward the focused subject.</summary>
    Focus,

    /// <summary>Away from the focused subject.</summary>
    AwayFromFocus,
}

/// <summary>What an Interact intent does.</summary>
public enum InteractVerb : byte
{
    /// <summary>Eat (satisfies Hunger).</summary>
    Eat,

    /// <summary>Rest (satisfies Rest).</summary>
    Rest,

    /// <summary>Spend time with others (satisfies Social).</summary>
    Socialize,

    /// <summary>Try to catch prey.</summary>
    Hunt,
}

/// <summary>
/// One node of a behaviour tree. Only the fields of its <see cref="Type"/> are used; the loader rejects missing or invalid ones.
/// </summary>
public sealed record BehaviourNodeDefinition
{
    /// <summary>Node kind.</summary>
    public required BehaviourNodeType Type { get; init; }

    /// <summary>Sequence, Selector, Parallel: children in order.</summary>
    public IReadOnlyList<BehaviourNodeDefinition>? Children { get; init; }

    /// <summary>Cooldown, Repeat: the decorated child.</summary>
    public BehaviourNodeDefinition? Child { get; init; }

    /// <summary>Parallel: finishing rule.</summary>
    public ParallelPolicy Policy { get; init; }

    /// <summary>Condition: what is compared (same inputs as utility considerations, 0–1000).</summary>
    public ConsiderationInput? Input { get; init; }

    /// <summary>Condition: comparison.</summary>
    public ConditionOperator Op { get; init; }

    /// <summary>Condition: the value compared against, 0–1000.</summary>
    public int Value { get; init; }

    /// <summary>Action: which intent.</summary>
    public IntentKind? Intent { get; init; }

    /// <summary>Action MoveTo: where to.</summary>
    public MoveTargetMode Target { get; init; }

    /// <summary>Action MoveTo: speed in percent of the species' walk speed (1–500).</summary>
    public int Speed { get; init; }

    /// <summary>Action MoveTo: ticks to wait for arrival before failing.</summary>
    public int Timeout { get; init; }

    /// <summary>Action Emote: which emote, e.g. <c>fear</c>.</summary>
    public Emote? Emote { get; init; }

    /// <summary>Action Interact: what to do.</summary>
    public InteractVerb Verb { get; init; }

    /// <summary>Wait, Cooldown: duration in ticks.</summary>
    public int Ticks { get; init; }

    /// <summary>Repeat: how many successes before succeeding (0 = until the child fails).</summary>
    public int Times { get; init; }
}

/// <summary>The behaviour tree that carries out one goal (AI Bible §7.3), loaded from <c>data/ai/bt/*.json</c>.</summary>
public sealed record BehaviourTreeDefinition
{
    /// <summary>Most nodes in one tree (fixed per-creature state, R21).</summary>
    public const int MaxNodes = 32;

    /// <summary>Deepest allowed nesting.</summary>
    public const int MaxDepth = 8;

    /// <summary>Key of the goal this tree executes; every goal has exactly one tree.</summary>
    public required string Goal { get; init; }

    /// <summary>The root node.</summary>
    public required BehaviourNodeDefinition Root { get; init; }
}
