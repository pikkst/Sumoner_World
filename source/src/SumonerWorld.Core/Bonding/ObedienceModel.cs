using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Cognition.Opinions;

namespace SumonerWorld.Core.Bonding;

/// <summary>How a creature answers its owner's command (AI Bible §7.4). The response is always shown (Pillar 1).</summary>
public enum CommandResponse : byte
{
    /// <summary>Immediate, clean execution.</summary>
    Obey,

    /// <summary>Does it its own way (e.g. flanks instead of charging).</summary>
    Improvise,

    /// <summary>Short pause with a Fear/Grudge emote, then obeys.</summary>
    Hesitate,

    /// <summary>Refusal emote; keeps its own goal.</summary>
    Refuse,
}

/// <summary>
/// The part of the obedience score that mattered most (M2-08, AI Bible §7.4: "the player learns why"): for an obeyed command the
/// biggest reason to obey, otherwise the biggest reason against. Values are sent to the AI Inspector: never renumber.
/// </summary>
public enum ObedienceReason : byte
{
    /// <summary>Nothing stood out.</summary>
    None = 0,

    /// <summary>Their bond (bond level).</summary>
    Bond = 1,

    /// <summary>It trusts the owner.</summary>
    Trust = 2,

    /// <summary>It does not trust the owner.</summary>
    Distrust = 3,

    /// <summary>It respects the owner.</summary>
    Respect = 4,

    /// <summary>It does not respect the owner.</summary>
    Disrespect = 5,

    /// <summary>It is loyal by nature.</summary>
    Loyal = 6,

    /// <summary>It is independent by nature.</summary>
    Independent = 7,

    /// <summary>It is afraid of the owner.</summary>
    Fear = 8,

    /// <summary>The command is dangerous.</summary>
    Danger = 9,

    /// <summary>The command goes against its memories (e.g. fighting its own kind).</summary>
    MemoryConflict = 10,

    /// <summary>The command is suicidal and it is not loyal enough.</summary>
    Suicidal = 11,

    /// <summary>It knows a better way (learning).</summary>
    BetterTactic = 12,
}

/// <summary>A command response and the obedience score behind it.</summary>
/// <param name="Response">What the creature does.</param>
/// <param name="Obedience">The score (higher = more willing).</param>
/// <param name="Suicidal">True if the command was refused as suicidal.</param>
/// <param name="Reason">What mattered most (M2-08).</param>
public readonly record struct ObedienceResult(CommandResponse Response, int Obedience, bool Suicidal, ObedienceReason Reason = ObedienceReason.None);

/// <summary>Weights and thresholds of the obedience formula (AI Bible §7.4). Starting values, tuned in the M1 playtest (R48).</summary>
public sealed class ObedienceSettings
{
    /// <summary>Score before anything else counts.</summary>
    public int Base { get; init; } = 450;

    /// <summary>Added per bond level.</summary>
    public int PerBondLevel { get; init; } = 60;

    /// <summary>Multiplier on Trust in the owner.</summary>
    public int TrustWeight { get; init; } = 2;

    /// <summary>Multiplier on Respect for the owner.</summary>
    public int RespectWeight { get; init; } = 2;

    /// <summary>Multiplier on the Loyalty trait.</summary>
    public int LoyaltyWeight { get; init; } = 1;

    /// <summary>Multiplier on Fear of the owner (subtracted).</summary>
    public int FearWeight { get; init; } = 2;

    /// <summary>Danger (0–1000) is divided by this and subtracted.</summary>
    public int DangerDivisor { get; init; } = 2;

    /// <summary>Subtracted when the command conflicts with the creature's own memories.</summary>
    public int MemoryConflictPenalty { get; init; } = 200;

    /// <summary>Obey at or above this score (AI Bible: 600).</summary>
    public int ObeyAt { get; init; } = 600;

    /// <summary>Improvise at or above this score (AI Bible: 400).</summary>
    public int ImproviseAt { get; init; } = 400;

    /// <summary>Hesitate at or above this score (AI Bible: 200); below it the creature refuses.</summary>
    public int HesitateAt { get; init; } = 200;

    /// <summary>A command this dangerous (0–1000) or more is suicidal.</summary>
    public int SuicidalDanger { get; init; } = 800;

    /// <summary>Creatures with Loyalty below this refuse suicidal commands (AI Bible: 50).</summary>
    public int SuicidalLoyaltyBelow { get; init; } = 50;

    /// <summary>The default settings.</summary>
    public static ObedienceSettings Default { get; } = new();
}

/// <summary>Decides how a bonded creature answers a command (AI Bible §7.4).</summary>
public interface IObedienceModel
{
    /// <summary>
    /// The creature's response to a validated command from <paramref name="owner"/>. <paramref name="danger"/> is 0–1000;
    /// <paramref name="knowsBetterTactic"/> comes from learning (AI Bible §8).
    /// </summary>
    ObedienceResult Evaluate(MindHandle creature, MemorySubject owner, int bondLevel, int danger, bool conflictsWithMemory, bool knowsBetterTactic, ulong now);
}

/// <summary>
/// <see cref="IObedienceModel"/>: <c>obedience = base + bond level + Trust + Respect + Loyalty − Fear − danger − memory conflict</c>
/// (weights in <see cref="ObedienceSettings"/>). A suicidal command from an owner of a creature with low Loyalty is refused.
/// </summary>
public sealed class ObedienceModel : IObedienceModel
{
    private const int MaxDanger = 1000;

    private readonly ICreatureMindStore _store;
    private readonly IOpinionModel _opinions;
    private readonly ObedienceSettings _settings;

    /// <summary>Creates the model.</summary>
    public ObedienceModel(ICreatureMindStore store, IOpinionModel opinions, ObedienceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(opinions);
        ArgumentNullException.ThrowIfNull(settings);

        _store = store;
        _opinions = opinions;
        _settings = settings;
    }

    /// <inheritdoc />
    public ObedienceResult Evaluate(MindHandle creature, MemorySubject owner, int bondLevel, int danger, bool conflictsWithMemory, bool knowsBetterTactic, ulong now)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bondLevel);
        danger = Math.Clamp(danger, 0, MaxDanger);

        Personality personality = _store.GetPersonality(creature);
        Opinion opinion = _opinions.GetOpinion(creature, owner, now);
        Span<(int Points, ObedienceReason For, ObedienceReason Against)> parts =
        [
            (bondLevel * _settings.PerBondLevel, ObedienceReason.Bond, ObedienceReason.None),
            (opinion.Trust * _settings.TrustWeight, ObedienceReason.Trust, ObedienceReason.Distrust),
            (opinion.Respect * _settings.RespectWeight, ObedienceReason.Respect, ObedienceReason.Disrespect),
            (personality.Loyalty * _settings.LoyaltyWeight, ObedienceReason.Loyal, ObedienceReason.Independent),
            (-(Math.Max(0, opinion.Fear) * _settings.FearWeight), ObedienceReason.None, ObedienceReason.Fear),
            (-(danger / _settings.DangerDivisor), ObedienceReason.None, ObedienceReason.Danger),
            (conflictsWithMemory ? -_settings.MemoryConflictPenalty : 0, ObedienceReason.None, ObedienceReason.MemoryConflict),
        ];
        int obedience = _settings.Base;
        foreach ((int points, _, _) in parts)
        {
            obedience += points;
        }

        if (danger >= _settings.SuicidalDanger && personality.Loyalty < _settings.SuicidalLoyaltyBelow)
        {
            return new ObedienceResult(CommandResponse.Refuse, obedience, Suicidal: true, ObedienceReason.Suicidal);
        }

        CommandResponse response =
            obedience >= _settings.ObeyAt && !knowsBetterTactic ? CommandResponse.Obey
            : obedience >= _settings.ImproviseAt ? CommandResponse.Improvise
            : obedience >= _settings.HesitateAt ? CommandResponse.Hesitate
            : CommandResponse.Refuse;
        ObedienceReason reason = response == CommandResponse.Improvise && obedience >= _settings.ObeyAt ? ObedienceReason.BetterTactic
            : MostImportant(parts, response == CommandResponse.Obey);
        return new ObedienceResult(response, obedience, Suicidal: false, reason);
    }

    // The largest part in favour (for an obeyed command) or against (otherwise).
    private static ObedienceReason MostImportant(ReadOnlySpan<(int Points, ObedienceReason For, ObedienceReason Against)> parts, bool inFavour)
    {
        (int best, ObedienceReason reason) = (0, ObedienceReason.None);
        foreach ((int points, ObedienceReason good, ObedienceReason bad) in parts)
        {
            int weight = inFavour ? points : -points;
            ObedienceReason candidate = inFavour ? good : bad;
            if (weight > best && candidate != ObedienceReason.None)
            {
                (best, reason) = (weight, candidate);
            }
        }

        return reason;
    }
}
