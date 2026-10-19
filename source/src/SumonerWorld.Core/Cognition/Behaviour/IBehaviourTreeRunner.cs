using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;

namespace SumonerWorld.Core.Cognition.Behaviour;

/// <summary>Result of ticking a behaviour tree or node.</summary>
public enum TreeStatus : byte
{
    /// <summary>Still working; tick again next time.</summary>
    Running,

    /// <summary>Finished successfully.</summary>
    Success,

    /// <summary>Finished unsuccessfully.</summary>
    Failure,
}

/// <summary>
/// An intent emitted by a behaviour tree action (AI Bible §7.3), published on the event bus. The simulation validates and carries
/// it out exactly like a player intent (R30), and reports MoveTo results back with <see cref="IBehaviourTreeRunner.ReportOutcome"/>.
/// </summary>
/// <param name="Actor">The creature.</param>
/// <param name="Kind">What it wants to do.</param>
/// <param name="Target">MoveTo: where to.</param>
/// <param name="Speed">MoveTo: percent of walk speed.</param>
/// <param name="Emote">Emote: which emote.</param>
/// <param name="Verb">Interact: what to do.</param>
/// <param name="Focus">The subject the creature is focused on, if any (MoveTo Focus/Away, Attack).</param>
public readonly record struct CreatureIntent(
    Entity Actor, IntentKind Kind, MoveTargetMode Target, int Speed, Emote Emote, InteractVerb Verb, MemorySubject? Focus);

/// <summary>What the runner needs for one tick of a creature's tree.</summary>
/// <param name="Actor">The creature's entity (put on emitted intents).</param>
/// <param name="Goal">The goal chosen by the utility AI; a change aborts the running tree.</param>
/// <param name="Now">Current tick.</param>
/// <param name="Focus">Focused subject, if any.</param>
/// <param name="FocusDistance">Distance to the focus, 0–1000.</param>
/// <param name="Health">Own health, 0–1000.</param>
public readonly record struct BehaviourContext(Entity Actor, ushort Goal, ulong Now, MemorySubject? Focus, int FocusDistance, int Health);

/// <summary>
/// Runs the data-defined behaviour tree of each creature's current goal (AI Bible §7.3). Trees are shared (flyweight); each
/// creature has a small fixed state. Changing the goal aborts the running tree (the AI Bible's "abort on goal change") and emits
/// a Stop intent.
/// </summary>
public interface IBehaviourTreeRunner
{
    /// <summary>Ticks the tree for <paramref name="context"/>.Goal. A finished tree restarts on the next tick.</summary>
    TreeStatus Tick(MindHandle mind, in BehaviourContext context);

    /// <summary>The simulation reports how the creature's pending MoveTo ended (arrived or blocked).</summary>
    void ReportOutcome(MindHandle mind, bool succeeded);
}
