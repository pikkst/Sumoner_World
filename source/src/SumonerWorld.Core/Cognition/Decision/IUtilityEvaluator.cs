using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Randomness;

namespace SumonerWorld.Core.Cognition.Decision;

/// <summary>The goals one species can choose, prepared once at start-up (AI Bible §7.1).</summary>
public sealed class GoalSet
{
    internal GoalSet(GoalDefinition[] goals)
    {
        Items = goals;
    }

    /// <summary>The species' goals, ordered by id.</summary>
    public IReadOnlyList<GoalDefinition> Goals => Items;

    // Array access in the hot path: iterating IReadOnlyList with foreach would box its enumerator (R21).
    internal GoalDefinition[] Items { get; }
}

/// <summary>What a creature is deciding about, gathered by the caller (perception, health) for one decision.</summary>
/// <param name="Goals">The creature's species goal set.</param>
/// <param name="Now">Current tick.</param>
/// <param name="CurrentGoal">Goal being pursued (0 = idle); gets the momentum bonus.</param>
/// <param name="Focus">The subject the creature is paying attention to, if any.</param>
/// <param name="FocusDistance">Distance to the focus, 0 (touching) to 1000 (edge of perception).</param>
/// <param name="Health">Own health, 0 to 1000.</param>
public readonly record struct DecisionContext(GoalSet Goals, ulong Now, ushort CurrentGoal, MemorySubject? Focus, int FocusDistance, int Health);

/// <summary>A chosen goal and its score; goal 0 means idle (nothing scored above 0, or no interrupt is needed).</summary>
/// <param name="Goal">Chosen goal id.</param>
/// <param name="Score">Its utility score.</param>
public readonly record struct Decision(ushort Goal, int Score)
{
    /// <summary>Goal id meaning "nothing to do / no change".</summary>
    public const ushort Idle = 0;
}

/// <summary>
/// Utility AI goal selection (AI Bible §7.2): every goal of the species is scored from the creature's needs, emotions, opinion of
/// its focus, health and time of day through data-defined curves; the best goal wins.
/// </summary>
public interface IUtilityEvaluator
{
    /// <summary>Prepares the goal set for a species.</summary>
    GoalSet ForSpecies(SpeciesDefinition species);

    /// <summary>Chooses a goal on a decision tick. <paramref name="random"/> is the creature's own stream (near-ties only).</summary>
    Decision Decide(MindHandle mind, in DecisionContext context, IRandom random);

    /// <summary>
    /// Between decision ticks, after a high-priority percept: returns an interrupt goal that beats the current goal's score, or
    /// <see cref="Decision.Idle"/> if the current goal should continue.
    /// </summary>
    Decision CheckInterrupt(MindHandle mind, in DecisionContext context);

    /// <summary>The score of one goal (for tests and the AI Inspector).</summary>
    int Score(MindHandle mind, ushort goal, in DecisionContext context);
}
