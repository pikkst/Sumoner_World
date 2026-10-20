using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Cognition.Opinions;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Randomness;

namespace SumonerWorld.Core.Cognition.Decision;

/// <summary>
/// <see cref="IUtilityEvaluator"/> with integer scoring (R11):
/// <c>score = weight × personalityModifier / 1000 × Π curve(input) / 1000</c>, +10% for the current goal (momentum).
/// Personality modifier = 1000 + Σ factor × trait, clamped to [0, 2000].
/// Selection: highest score; an exact tie goes to the lower id; if the runner-up is within 5%, the creature's own random
/// stream picks between the two, weighted by score. Does not allocate (R21).
/// </summary>
public sealed class UtilityEvaluator : IUtilityEvaluator
{
    private const int PerMille = 1000;
    private const int MaxModifier = 2000;
    private const int MomentumDivisor = 10;
    private const int NearTiePercent = 95;

    private readonly ICreatureMindStore _store;
    private readonly Dictionary<string, GoalDefinition> _goalsByKey;
    private readonly InputSampler _sampler;
    private readonly InputSampler _peekSampler;

    /// <summary>Creates the evaluator over all loaded goals.</summary>
    public UtilityEvaluator(
        ICreatureMindStore store, IAppraisal appraisal, IOpinionModel opinions, IEnumerable<GoalDefinition> goals, int ticksPerDay)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(appraisal);
        ArgumentNullException.ThrowIfNull(opinions);
        ArgumentNullException.ThrowIfNull(goals);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ticksPerDay);

        _store = store;
        _sampler = new InputSampler(store, appraisal, opinions, ticksPerDay);
        _peekSampler = new InputSampler(store, appraisal, opinions, ticksPerDay, readOnly: true);
        _goalsByKey = goals.ToDictionary(goal => goal.Key, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public GoalSet ForSpecies(SpeciesDefinition species)
    {
        ArgumentNullException.ThrowIfNull(species);
        return new GoalSet([.. species.Goals.Select(key => _goalsByKey.TryGetValue(key, out GoalDefinition? goal)
            ? goal
            : throw new ArgumentException($"Species '{species.Id}' uses unknown goal '{key}'.", nameof(species)))
            .OrderBy(goal => goal.Id)]);
    }

    /// <inheritdoc />
    public Decision Decide(MindHandle mind, in DecisionContext context, IRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        ReadOnlySpan<int> inputs = Sample(mind, context);
        Personality personality = _store.GetPersonality(mind);

        var best = new Decision(Decision.Idle, 0);
        var second = new Decision(Decision.Idle, 0);
        foreach (GoalDefinition goal in context.Goals.Items)
        {
            var candidate = new Decision(goal.Id, ScoreGoal(goal, personality, context.CurrentGoal, inputs));
            if (candidate.Score > best.Score)
            {
                second = best;
                best = candidate;
            }
            else if (candidate.Score > second.Score)
            {
                second = candidate;
            }
        }

        bool nearTie = second.Score > 0 && second.Score != best.Score && (long)second.Score * 100 >= (long)best.Score * NearTiePercent;
        if (nearTie && random.NextInt(0, best.Score + second.Score) >= best.Score)
        {
            return second;
        }

        return best;
    }

    /// <inheritdoc />
    public Decision CheckInterrupt(MindHandle mind, in DecisionContext context)
    {
        ReadOnlySpan<int> inputs = Sample(mind, context);
        Personality personality = _store.GetPersonality(mind);

        int currentScore = 0;
        var best = new Decision(Decision.Idle, 0);
        foreach (GoalDefinition goal in context.Goals.Items)
        {
            if (goal.Id == context.CurrentGoal)
            {
                currentScore = ScoreGoal(goal, personality, context.CurrentGoal, inputs);
            }
            else if (goal.Interrupt)
            {
                int score = ScoreGoal(goal, personality, context.CurrentGoal, inputs);
                if (score > best.Score)
                {
                    best = new Decision(goal.Id, score);
                }
            }
        }

        return best.Score > currentScore ? best : new Decision(Decision.Idle, 0);
    }

    /// <inheritdoc />
    public int Score(MindHandle mind, ushort goal, in DecisionContext context)
    {
        ArgumentNullException.ThrowIfNull(context.Goals);
        ReadOnlySpan<int> inputs = _peekSampler.Sample(mind, context.Now, context.Focus, context.FocusDistance, context.Health);
        Personality personality = _store.GetPersonality(mind);
        foreach (GoalDefinition definition in context.Goals.Items)
        {
            if (definition.Id == goal)
            {
                return ScoreGoal(definition, personality, context.CurrentGoal, inputs);
            }
        }

        throw new ArgumentOutOfRangeException(nameof(goal), goal, "Goal is not in this species' goal set.");
    }

    private static int ScoreGoal(GoalDefinition goal, Personality personality, ushort currentGoal, ReadOnlySpan<int> inputs)
    {
        // Indexed loops: foreach over IReadOnlyList would box an enumerator per call (R21).
        long modifier = PerMille;
        for (int i = 0; i < goal.Personality.Count; i++)
        {
            PersonalityFactor factor = goal.Personality[i];
            modifier += (long)factor.Factor * personality.Get(factor.Trait);
        }

        long score = goal.Weight * Math.Clamp(modifier, 0, MaxModifier) / PerMille;
        for (int i = 0; i < goal.Considerations.Count; i++)
        {
            ConsiderationDefinition consideration = goal.Considerations[i];
            score = score * ResponseCurve.Evaluate(consideration.Curve, inputs[(int)consideration.Input]) / PerMille;
            if (score == 0)
            {
                return 0;
            }
        }

        if (goal.Id == currentGoal)
        {
            score += score / MomentumDivisor;
        }

        return (int)score;
    }

    private ReadOnlySpan<int> Sample(MindHandle mind, in DecisionContext context)
    {
        ArgumentNullException.ThrowIfNull(context.Goals);
        return _sampler.Sample(mind, context.Now, context.Focus, context.FocusDistance, context.Health);
    }
}
