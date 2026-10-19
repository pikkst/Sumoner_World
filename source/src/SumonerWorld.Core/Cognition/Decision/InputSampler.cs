using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Cognition.Opinions;
using SumonerWorld.Core.Data.Definitions;

namespace SumonerWorld.Core.Cognition.Decision;

/// <summary>
/// Reads every <see cref="ConsiderationInput"/> for a creature, normalised to [0, 1000] (AI Bible §7.2). Shared by utility
/// scoring and behaviour tree conditions so both see the same values (R47). One preallocated buffer; simulation thread only.
/// </summary>
public sealed class InputSampler
{
    /// <summary>Number of inputs (indexable by <see cref="ConsiderationInput"/>).</summary>
    public const int Count = (int)ConsiderationInput.TimeOfDay + 1;

    private const int PerMille = 1000;
    private const int ToPerMille = 10;
    private const int OpinionOffset = 100;
    private const int OpinionScale = 5;

    private readonly ICreatureMindStore _store;
    private readonly IAppraisal _appraisal;
    private readonly IOpinionModel _opinions;
    private readonly int _ticksPerDay;
    private readonly int[] _inputs = new int[Count];

    /// <summary>Creates the sampler.</summary>
    public InputSampler(ICreatureMindStore store, IAppraisal appraisal, IOpinionModel opinions, int ticksPerDay)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(appraisal);
        ArgumentNullException.ThrowIfNull(opinions);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ticksPerDay);

        _store = store;
        _appraisal = appraisal;
        _opinions = opinions;
        _ticksPerDay = ticksPerDay;
    }

    /// <summary>
    /// Samples all inputs. The returned span is overwritten by the next call. <paramref name="focusDistance"/> and
    /// <paramref name="health"/> are 0–1000.
    /// </summary>
    public ReadOnlySpan<int> Sample(MindHandle mind, ulong now, MemorySubject? focus, int focusDistance, int health)
    {
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

        Opinion opinion = focus is MemorySubject subject ? _opinions.GetOpinion(mind, subject, now) : Opinion.Neutral;
        _inputs[(int)ConsiderationInput.HasTarget] = focus is null ? 0 : PerMille;
        _inputs[(int)ConsiderationInput.TargetTrust] = (opinion.Trust + OpinionOffset) * OpinionScale;
        _inputs[(int)ConsiderationInput.TargetFear] = (opinion.Fear + OpinionOffset) * OpinionScale;
        _inputs[(int)ConsiderationInput.TargetAffection] = (opinion.Affection + OpinionOffset) * OpinionScale;
        _inputs[(int)ConsiderationInput.TargetRespect] = (opinion.Respect + OpinionOffset) * OpinionScale;
        _inputs[(int)ConsiderationInput.TargetDistance] = Math.Clamp(focusDistance, 0, PerMille);
        _inputs[(int)ConsiderationInput.Health] = Math.Clamp(health, 0, PerMille);
        _inputs[(int)ConsiderationInput.TimeOfDay] = (int)(now % (ulong)_ticksPerDay * PerMille / (ulong)_ticksPerDay);
        return _inputs;
    }
}
