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
    private const int InputCount = (int)ConsiderationInput.TimeOfDay + 1;
    private const int PerMille = 1000;
    private const int MaxModifier = 2000;
    private const int MomentumDivisor = 10;
    private const int NearTiePercent = 95;
    private const int ToPerMille = 10;
    private const int OpinionOffset = 100;
    private const int OpinionScale = 5;

    private readonly ICreatureMindStore _store;
    private readonly IAppraisal _appraisal;
    private readonly IOpinionModel _opinions;
    private readonly Dictionary<string, GoalDefinition> _goalsByKey;
    private readonly int _ticksPerDay;
    private readonly int[] _inputs = new int[InputCount];

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
        _appraisal = appraisal;
        _opinions = opinions;
        _goalsByKey = goals.ToDictionary(goal => goal.Key, StringComparer.Ordinal);
        _ticksPerDay = ticksPerDay;
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
        GatherInputs(mind, context);
        Personality personality = _store.GetPersonality(mind);

        var best = new Decision(Decision.Idle, 0);
        var second = new Decision(Decision.Idle, 0);
        foreach (GoalDefinition goal in context.Goals.Items)
        {
            var candidate = new Decision(goal.Id, ScoreGoal(goal, personality, context.CurrentGoal));
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
        GatherInputs(mind, context);
        Personality personality = _store.GetPersonality(mind);

        int currentScore = 0;
        var best = new Decision(Decision.Idle, 0);
        foreach (GoalDefinition goal in context.Goals.Items)
        {
            if (goal.Id == context.CurrentGoal)
            {
                currentScore = ScoreGoal(goal, personality, context.CurrentGoal);
            }
            else if (goal.Interrupt)
            {
                int score = ScoreGoal(goal, personality, context.CurrentGoal);
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
        GatherInputs(mind, context);
        Personality personality = _store.GetPersonality(mind);
        foreach (GoalDefinition definition in context.Goals.Items)
        {
            if (definition.Id == goal)
            {
                return ScoreGoal(definition, personality, context.CurrentGoal);
            }
        }

        throw new ArgumentOutOfRangeException(nameof(goal), goal, "Goal is not in this species' goal set.");
    }

    private int ScoreGoal(GoalDefinition goal, Personality personality, ushort currentGoal)
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
            score = score * ResponseCurve.Evaluate(consideration.Curve, _inputs[(int)consideration.Input]) / PerMille;
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

    private void GatherInputs(MindHandle mind, in DecisionContext context)
    {
        ArgumentNullException.ThrowIfNull(context.Goals);
        ulong now = context.Now;
        _inputs[(int)ConsiderationInput.Constant] = PerMille;
        _inputs[(int)ConsiderationInput.NeedHunger] = _store.GetNeed(mind, Need.Hunger, now) * ToPerMille;
        _inputs[(int)ConsiderationInput.NeedRest] = _store.GetNeed(mind, Need.Rest, now) * ToPerMille;
        _inputs[(int)ConsiderationInput.NeedSafety] = _store.GetNeed(mind, Need.Safety, now) * ToPerMille;
        _inputs[(int)ConsiderationInput.NeedSocial] = _store.GetNeed(mind, Need.Social, now) * ToPerMille;
        _inputs[(int)ConsiderationInput.NeedTerritory] = _store.GetNeed(mind, Need.Territory, now) * ToPerMille;

        EmotionState emotions = _appraisal.GetEmotions(mind, now);
        _inputs[(int)ConsiderationInput.EmotionFear] = emotions.Fear * ToPerMille;
        _inputs[(int)ConsiderationInput.EmotionAnger] = emotions.Anger * ToPerMille;
        _inputs[(int)ConsiderationInput.EmotionJoy] = emotions.Joy * ToPerMille;
        _inputs[(int)ConsiderationInput.EmotionCuriosity] = emotions.Curiosity * ToPerMille;
        _inputs[(int)ConsiderationInput.EmotionStress] = emotions.Stress * ToPerMille;

        Opinion opinion = context.Focus is MemorySubject focus ? _opinions.GetOpinion(mind, focus, now) : Opinion.Neutral;
        _inputs[(int)ConsiderationInput.HasTarget] = context.Focus is null ? 0 : PerMille;
        _inputs[(int)ConsiderationInput.TargetTrust] = (opinion.Trust + OpinionOffset) * OpinionScale;
        _inputs[(int)ConsiderationInput.TargetFear] = (opinion.Fear + OpinionOffset) * OpinionScale;
        _inputs[(int)ConsiderationInput.TargetAffection] = (opinion.Affection + OpinionOffset) * OpinionScale;
        _inputs[(int)ConsiderationInput.TargetRespect] = (opinion.Respect + OpinionOffset) * OpinionScale;
        _inputs[(int)ConsiderationInput.TargetDistance] = Math.Clamp(context.FocusDistance, 0, PerMille);
        _inputs[(int)ConsiderationInput.Health] = Math.Clamp(context.Health, 0, PerMille);
        _inputs[(int)ConsiderationInput.TimeOfDay] = (int)(now % (ulong)_ticksPerDay * PerMille / (ulong)_ticksPerDay);
    }
}
