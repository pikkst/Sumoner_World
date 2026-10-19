using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Cognition.Opinions;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Core.Jobs;

namespace SumonerWorld.Core.Ecosystem;

/// <summary>Whose reputation with whom: a player's standing with one species in one region (AI Bible §4.5).</summary>
/// <param name="Species">Species index (position in the data registry's species list).</param>
/// <param name="Region">Coarse region id chosen by the world (e.g. a 256 m cell).</param>
/// <param name="Player">Player account id.</param>
public readonly record struct ReputationKey(ushort Species, int Region, ulong Player);

/// <summary>A player's reputation with a species in a region.</summary>
/// <param name="Player">Player account id.</param>
/// <param name="Score">Reputation in [−1000, 1000].</param>
public readonly record struct PlayerReputation(ulong Player, int Score);

/// <summary>
/// Server-wide memory of a species about players (AI Bible §4.5): how a herd "remembers" an over-hunter. Events are queued as they
/// happen and summed in a background job (R17); scores are clamped to [−1000, 1000].
/// </summary>
public interface IReputationService
{
    /// <summary>Queues the reputation effect of a memory event (types without a <c>reputationDelta</c> are ignored).</summary>
    void Record(ushort eventType, ReputationKey key);

    /// <summary>The current score; 0 when unknown.</summary>
    int GetScore(ReputationKey key);

    /// <summary>Writes the players with a non-zero score for this species and region into <paramref name="results"/>; returns how many.</summary>
    int KnownPlayers(ushort species, int region, Span<PlayerReputation> results);

    /// <summary>Sends queued events to the aggregation job (call at low frequency, e.g. every 10 s).</summary>
    void Flush();

    /// <summary>
    /// Gives a newly spawned wild creature its kind's opinion of every player known in its region (AI Bible §4.5): Trust and
    /// Affection follow the score, Fear rises with a bad score. Returns how many players were seeded.
    /// </summary>
    int SeedOpinions(ICreatureMindStore store, MindHandle creature, ushort species, int region);
}

/// <summary>
/// <see cref="IReputationService"/> with fixed-size buffers: events go to a pending buffer; <see cref="Flush"/> hands the buffer to a
/// reusable job that sums it off-thread; the sums are added to the table when jobs are applied (next tick). If the pending buffer
/// fills up between flushes, it is summed on the spot instead (never dropped, R36). No allocation in steady state (R21).
/// </summary>
public sealed class ReputationService : IReputationService
{
    /// <summary>Lowest and highest score.</summary>
    public const int MaxScore = 1000;

    private const int ScoreToTrust = 20;
    private const int ScoreToAffection = 40;

    private readonly MemoryRules _rules;
    private readonly IJobScheduler _jobs;
    private readonly Dictionary<ReputationKey, int> _scores;
    private readonly AggregationJob[] _jobPool;
    private readonly PlayerReputation[] _seedBuffer;
    private (ReputationKey Key, int Delta)[] _pending;
    private int _pendingCount;
    private int _nextJob;

    /// <summary>Creates the service. <paramref name="pendingCapacity"/> events can queue between flushes.</summary>
    public ReputationService(MemoryRules rules, IJobScheduler jobs, int pendingCapacity, int tableCapacity)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pendingCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tableCapacity);

        _rules = rules;
        _jobs = jobs;
        _scores = new Dictionary<ReputationKey, int>(tableCapacity);
        _pending = new (ReputationKey, int)[pendingCapacity];
        _jobPool = [new AggregationJob(this, pendingCapacity), new AggregationJob(this, pendingCapacity)];
        _seedBuffer = new PlayerReputation[ICreatureMindStore.OpinionCapacity];

        // Create the key comparer now (first use allocates), not in the middle of a running game (R21).
        _scores[default] = 0;
        _scores.Remove(default);
    }

    /// <summary>Events queued and not yet summed.</summary>
    public int PendingEvents => _pendingCount;

    /// <summary>Aggregation jobs applied so far.</summary>
    public int Aggregations { get; private set; }

    /// <inheritdoc />
    public void Record(ushort eventType, ReputationKey key)
    {
        int delta = _rules.ReputationDelta(eventType);
        if (delta == 0)
        {
            return;
        }

        if (_pendingCount == _pending.Length)
        {
            Sum(_pending, _pendingCount);
            _pendingCount = 0;
        }

        _pending[_pendingCount++] = (key, delta);
    }

    /// <inheritdoc />
    public int GetScore(ReputationKey key) => _scores.GetValueOrDefault(key);

    /// <inheritdoc />
    public int KnownPlayers(ushort species, int region, Span<PlayerReputation> results)
    {
        int count = 0;
        foreach (KeyValuePair<ReputationKey, int> entry in _scores)
        {
            if (entry.Key.Species == species && entry.Key.Region == region && entry.Value != 0 && count < results.Length)
            {
                results[count++] = new PlayerReputation(entry.Key.Player, entry.Value);
            }
        }

        // Dictionary order is not part of the contract: sort by account id so callers see a deterministic order (R11).
        results[..count].Sort(static (a, b) => a.Player.CompareTo(b.Player));
        return count;
    }

    /// <inheritdoc />
    public void Flush()
    {
        if (_pendingCount == 0)
        {
            return;
        }

        AggregationJob job = _jobPool[_nextJob];
        if (job.InFlight)
        {
            // Both jobs are still waiting to be applied (two flushes in one tick): sum on the spot instead.
            Sum(_pending, _pendingCount);
            _pendingCount = 0;
            return;
        }

        _pending = job.Swap(_pending, _pendingCount);
        _pendingCount = 0;
        _nextJob = (_nextJob + 1) % _jobPool.Length;
        _jobs.Submit(job);
    }

    /// <inheritdoc />
    public int SeedOpinions(ICreatureMindStore store, MindHandle creature, ushort species, int region)
    {
        ArgumentNullException.ThrowIfNull(store);
        int count = KnownPlayers(species, region, _seedBuffer);
        for (int i = 0; i < count; i++)
        {
            int score = _seedBuffer[i].Score;
            var baseline = new Opinion(score / ScoreToTrust, Math.Max(0, -score) / ScoreToTrust, score / ScoreToAffection, 0);
            store.SeedOpinionBaseline(creature, new MemorySubject(SubjectKind.Player, _seedBuffer[i].Player), baseline);
        }

        return count;
    }

    /// <summary>Adds the reputation table to a determinism hash, independent of dictionary order (TDD §2.3).</summary>
    public void AddTo(ref StateHash hash)
    {
        ulong combined = 0;
        foreach (KeyValuePair<ReputationKey, int> entry in _scores)
        {
            var one = default(StateHash);
            one.Add(entry.Key.Species);
            one.Add((ulong)(uint)entry.Key.Region);
            one.Add(entry.Key.Player);
            one.Add((ulong)(uint)entry.Value);
            combined += one.Value;
        }

        hash.Add(combined);
        hash.Add((ulong)_pendingCount);
    }

    private void Sum((ReputationKey Key, int Delta)[] events, int count)
    {
        for (int i = 0; i < count; i++)
        {
            Add(events[i].Key, events[i].Delta);
        }
    }

    private void Add(ReputationKey key, int delta) =>
        _scores[key] = Math.Clamp(_scores.GetValueOrDefault(key) + delta, -MaxScore, MaxScore);

    // Sums one batch of events off-thread (Execute) and adds the totals to the table on the simulation thread (Apply).
    private sealed class AggregationJob(ReputationService owner, int capacity) : IJob
    {
        private readonly Dictionary<ReputationKey, int> _totals = new(capacity);
        private readonly List<ReputationKey> _order = new(capacity);
        private (ReputationKey Key, int Delta)[] _events = new (ReputationKey, int)[capacity];
        private int _count;

        public bool InFlight { get; private set; }

        public (ReputationKey Key, int Delta)[] Swap((ReputationKey Key, int Delta)[] events, int count)
        {
            (ReputationKey Key, int Delta)[] free = _events;
            _events = events;
            _count = count;
            InFlight = true;
            return free;
        }

        public void Execute()
        {
            _totals.Clear();
            _order.Clear();
            for (int i = 0; i < _count; i++)
            {
                (ReputationKey key, int delta) = _events[i];
                if (_totals.TryAdd(key, delta))
                {
                    _order.Add(key);
                }
                else
                {
                    _totals[key] += delta;
                }
            }
        }

        public void Apply()
        {
            // In first-seen order: the clamping result does not depend on dictionary order (R11).
            foreach (ReputationKey key in _order)
            {
                owner.Add(key, _totals[key]);
            }

            owner.Aggregations++;
            InFlight = false;
        }
    }
}
