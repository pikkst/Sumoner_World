namespace SumonerWorld.Core.Cognition.Lod;

/// <summary>AI level-of-detail tiers (AI Bible §9.1).</summary>
public enum AiLodTier : byte
{
    /// <summary>T0: in combat, near a player or summoned. Full rates.</summary>
    Active,

    /// <summary>T1: within the near radius of a player.</summary>
    Near,

    /// <summary>T2: loaded but far from every player.</summary>
    Far,

    /// <summary>T3: in an unloaded chunk; only the population model runs (AI Bible §9.4).</summary>
    Abstract,
}

/// <summary>The AI work that is rate-limited per tier.</summary>
public enum AiActivity : byte
{
    /// <summary>Sensing the surroundings.</summary>
    Perception,

    /// <summary>Utility goal selection.</summary>
    Decision,

    /// <summary>Behaviour tree ticks.</summary>
    Behaviour,
}

/// <summary>Tier radii and update intervals (AI Bible §9.1). Starting values; tuned with the M1 benchmark (R48).</summary>
public sealed class AiLodSettings
{
    /// <summary>Within this many metres of a player a creature is Active (T0).</summary>
    public float ActiveRadius { get; init; } = 40f;

    /// <summary>Within this many metres of a player a creature is Near (T1).</summary>
    public float NearRadius { get; init; } = 120f;

    /// <summary>Extra distance before a creature drops to a lower tier, so tiers do not flicker at a boundary.</summary>
    public float Hysteresis { get; init; } = 10f;

    /// <summary>Ticks between updates, per tier (Active, Near, Far) and activity (Perception, Decision, Behaviour), at 30 Hz.</summary>
    public IReadOnlyList<IReadOnlyList<int>> Intervals { get; init; } =
    [
        [3, 6, 1],    // T0: perception 10 Hz, decision 5 Hz, behaviour every tick
        [8, 15, 3],   // T1: ~4 Hz, 2 Hz, 10 Hz
        [30, 60, 15], // T2: 1 Hz, 0.5 Hz, 2 Hz
    ];

    /// <summary>The default settings.</summary>
    public static AiLodSettings Default { get; } = new();
}

/// <summary>Chooses each creature's AI tier and spreads its work over ticks (AI Bible §9.1–9.2).</summary>
public interface IAiLodPolicy
{
    /// <summary>
    /// The tier for a creature given its current tier, the distance to the nearest player (<see cref="float.PositiveInfinity"/> if
    /// none), whether it is engaged (in combat or summoned) and whether its chunk is loaded.
    /// </summary>
    AiLodTier Classify(AiLodTier current, float nearestPlayerDistance, bool engaged, bool loaded);

    /// <summary>
    /// True if <paramref name="activity"/> should run for this creature on <paramref name="tick"/>. Creatures are spread over the
    /// interval by <paramref name="creatureIndex"/>, so the load per tick is even and the order is deterministic (R11).
    /// </summary>
    bool IsDue(AiLodTier tier, AiActivity activity, int creatureIndex, ulong tick);
}

/// <summary>Distance-based <see cref="IAiLodPolicy"/> with hysteresis (no allocation, R21).</summary>
public sealed class AiLodPolicy : IAiLodPolicy
{
    private const int TierCount = 3;
    private const int ActivityCount = 3;

    private readonly AiLodSettings _settings;
    private readonly int[] _intervals = new int[TierCount * ActivityCount];

    /// <summary>Creates the policy and validates the settings.</summary>
    public AiLodPolicy(AiLodSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!(settings.ActiveRadius > 0 && settings.NearRadius > settings.ActiveRadius && settings.Hysteresis >= 0))
        {
            throw new ArgumentException("AI LOD radii must satisfy 0 < active < near and hysteresis >= 0.", nameof(settings));
        }

        if (settings.Intervals.Count != TierCount || settings.Intervals.Any(row => row.Count != ActivityCount || row.Any(interval => interval < 1)))
        {
            throw new ArgumentException($"AI LOD intervals must be {TierCount} tiers x {ActivityCount} activities, each at least 1 tick.", nameof(settings));
        }

        _settings = settings;
        for (int tier = 0; tier < TierCount; tier++)
        {
            for (int activity = 0; activity < ActivityCount; activity++)
            {
                _intervals[(tier * ActivityCount) + activity] = settings.Intervals[tier][activity];
            }
        }
    }

    /// <inheritdoc />
    public AiLodTier Classify(AiLodTier current, float nearestPlayerDistance, bool engaged, bool loaded)
    {
        if (!loaded)
        {
            return AiLodTier.Abstract;
        }

        if (engaged)
        {
            return AiLodTier.Active;
        }

        // A creature keeps its better tier until it is clearly past the boundary (hysteresis).
        float activeLimit = _settings.ActiveRadius + (current == AiLodTier.Active ? _settings.Hysteresis : 0f);
        float nearLimit = _settings.NearRadius + (current <= AiLodTier.Near ? _settings.Hysteresis : 0f);
        if (nearestPlayerDistance <= activeLimit)
        {
            return AiLodTier.Active;
        }

        return nearestPlayerDistance <= nearLimit ? AiLodTier.Near : AiLodTier.Far;
    }

    /// <inheritdoc />
    public bool IsDue(AiLodTier tier, AiActivity activity, int creatureIndex, ulong tick)
    {
        if (tier == AiLodTier.Abstract)
        {
            return false;
        }

        int interval = _intervals[((int)tier * ActivityCount) + (int)activity];
        return tick % (ulong)interval == (ulong)((uint)creatureIndex % (uint)interval);
    }
}
