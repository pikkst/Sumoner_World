using SumonerWorld.Core.Cognition.Opinions;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Diagnostics;

namespace SumonerWorld.Core.Cognition.Memory;

/// <summary>
/// Pooled <see cref="ICreatureMindStore"/>: one flat record array with <see cref="ICreatureMindStore.CoreCapacity"/> +
/// <see cref="ICreatureMindStore.EpisodicCapacity"/> slots per mind, allocated once (R21). Records are replaced in place, so slot
/// order, eviction and recall order are fully determined by the sequence of calls (R11).
/// </summary>
public sealed class CreatureMindStore : ICreatureMindStore
{
    private const int SlotsPerMind = ICreatureMindStore.CoreCapacity + ICreatureMindStore.EpisodicCapacity;
    private const int ReinforceNumerator = 6;
    private const int ReinforceDenominator = 10;
    private const int NeedCount = 5;
    private const int MilliPoints = 1000;
    private const int BaselineTenthsPerPoint = 10;
    private const int MaxBaselineTenths = ICreatureMindStore.MaxBaseline * BaselineTenthsPerPoint;

    private readonly MemoryRules _rules;
    private readonly MemoryRecord[] _records;
    private readonly int[] _coreCounts;
    private readonly int[] _episodicCounts;
    private readonly Personality[] _birth;
    private readonly Personality[] _current;
    private readonly int[] _generations;
    private readonly bool[] _alive;
    private readonly int[] _freeSlots;
    private readonly MemorySubject[] _opinionSubjects;
    private readonly Opinion[] _opinionBaselineTenths;
    private readonly int[] _opinionCounts;
    private readonly int[] _needMilli;
    private readonly uint[] _needTicks;
    private readonly int[] _needRates;
    private int _freeCount;
    private int _nextUnusedSlot;

    /// <summary>Creates a store for at most <paramref name="capacity"/> creatures.</summary>
    public CreatureMindStore(MemoryRules rules, int capacity)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        _rules = rules;
        Capacity = capacity;
        _records = new MemoryRecord[capacity * SlotsPerMind];
        _coreCounts = new int[capacity];
        _episodicCounts = new int[capacity];
        _birth = new Personality[capacity];
        _current = new Personality[capacity];
        _generations = new int[capacity];
        _alive = new bool[capacity];
        _freeSlots = new int[capacity];
        _opinionSubjects = new MemorySubject[capacity * ICreatureMindStore.OpinionCapacity];
        _opinionBaselineTenths = new Opinion[capacity * ICreatureMindStore.OpinionCapacity];
        _opinionCounts = new int[capacity];
        _needMilli = new int[capacity * NeedCount];
        _needTicks = new uint[capacity * NeedCount];
        _needRates = new int[capacity * NeedCount];
    }

    /// <inheritdoc />
    public int Capacity { get; }

    /// <inheritdoc />
    public int Count { get; private set; }

    /// <inheritdoc />
    public MindHandle Allocate(Personality birth, NeedRates needRates)
    {
        ArgumentNullException.ThrowIfNull(needRates);

        int slot;
        if (_freeCount > 0)
        {
            slot = _freeSlots[--_freeCount];
        }
        else if (_nextUnusedSlot < Capacity)
        {
            slot = _nextUnusedSlot++;
        }
        else
        {
            throw new InvalidOperationException($"Creature mind capacity {Capacity} reached. Raise it in the server config.");
        }

        _alive[slot] = true;
        _birth[slot] = birth;
        _current[slot] = birth;
        _coreCounts[slot] = 0;
        _episodicCounts[slot] = 0;
        _opinionCounts[slot] = 0;
        int needStart = slot * NeedCount;
        Array.Clear(_needMilli, needStart, NeedCount);
        Array.Clear(_needTicks, needStart, NeedCount);
        _needRates[needStart + (int)Need.Hunger] = needRates.Hunger;
        _needRates[needStart + (int)Need.Rest] = needRates.Rest;
        _needRates[needStart + (int)Need.Safety] = needRates.Safety;
        _needRates[needStart + (int)Need.Social] = needRates.Social;
        _needRates[needStart + (int)Need.Territory] = needRates.Territory;
        Count++;
        return new MindHandle(slot, _generations[slot]);
    }

    /// <inheritdoc />
    public void Release(MindHandle mind)
    {
        EnsureValid(mind);
        Array.Clear(_records, mind.Slot * SlotsPerMind, SlotsPerMind);
        Array.Clear(_opinionBaselineTenths, mind.Slot * ICreatureMindStore.OpinionCapacity, ICreatureMindStore.OpinionCapacity);
        _alive[mind.Slot] = false;
        _generations[mind.Slot]++;
        _freeSlots[_freeCount++] = mind.Slot;
        Count--;
    }

    /// <inheritdoc />
    public bool IsValid(MindHandle mind) =>
        (uint)mind.Slot < (uint)Capacity && _alive[mind.Slot] && _generations[mind.Slot] == mind.Generation;

    /// <inheritdoc />
    public Personality GetBirthPersonality(MindHandle mind)
    {
        EnsureValid(mind);
        return _birth[mind.Slot];
    }

    /// <inheritdoc />
    public Personality GetPersonality(MindHandle mind)
    {
        EnsureValid(mind);
        return _current[mind.Slot];
    }

    /// <inheritdoc />
    public RememberResult Remember(MindHandle mind, ushort eventType, MemorySubject subject, ulong now)
    {
        EnsureValid(mind);
        MemoryEventTypeDefinition definition = _rules.Get(eventType);
        if (!_rules.AllowsSubject(eventType, subject.Kind))
        {
            throw new ArgumentException($"Memory event {definition.Key} cannot be about a {subject.Kind}.", nameof(subject));
        }

        uint tick = ToRecordTick(now);
        AdjustBaseline(mind, subject, _rules.OpinionWeights(eventType), definition.BaseSalience);
        int coreStart = mind.Slot * SlotsPerMind;
        int episodicStart = coreStart + ICreatureMindStore.CoreCapacity;

        int existing = FindIndex(coreStart, _coreCounts[mind.Slot], eventType, subject);
        if (existing < 0)
        {
            existing = FindIndex(episodicStart, _episodicCounts[mind.Slot], eventType, subject);
        }

        var tags = definition.IsCore ? MemoryTags.Core : MemoryTags.None;
        var record = new MemoryRecord(eventType, subject, (sbyte)definition.Valence, (ushort)definition.BaseSalience, tick, tick, 1, tags);

        // First-hand experience replaces a rumour of the same event (rumours only ever sit in episodic slots).
        if (existing >= 0 && (_records[existing].Tags & MemoryTags.Rumour) != 0)
        {
            if (!definition.IsCore)
            {
                int strength = Math.Max(definition.BaseSalience, Effective(_records[existing], tick));
                _records[existing] = record with { Salience = (ushort)Math.Min(strength, ICreatureMindStore.MaxSalience) };
                return new RememberResult(MemoryOutcome.Created, EvictedAnother: false, DriftedTrait: null);
            }

            RemoveEpisodic(mind, existing);
            existing = -1;
        }

        if (existing >= 0 && Effective(_records[existing], tick) >= ICreatureMindStore.ForgetThreshold)
        {
            _records[existing] = Reinforce(_records[existing], definition, tick);
            return new RememberResult(MemoryOutcome.Reinforced, EvictedAnother: false, DriftedTrait: null);
        }

        if (existing >= 0)
        {
            // Same event and subject, but forgotten: start again in the same slot.
            _records[existing] = record with { Tags = (_records[existing].Tags & MemoryTags.Pinned) | tags };
            return new RememberResult(MemoryOutcome.Created, EvictedAnother: false, DriftedTrait: null);
        }

        if (definition.IsCore && TryPin(mind, record, tick, out bool evicted))
        {
            PersonalityTrait? drifted = ApplyDrift(mind, definition);
            return new RememberResult(MemoryOutcome.CoreFormed, evicted, drifted);
        }

        bool evictedEpisodic = AddEpisodic(mind, record, tick);
        return new RememberResult(MemoryOutcome.Created, evictedEpisodic, DriftedTrait: null);
    }

    /// <inheritdoc />
    public int Recall(MindHandle mind, ulong now, Span<RecalledMemory> results) =>
        Collect(mind, now, subject: null, results);

    /// <inheritdoc />
    public int RecallAbout(MindHandle mind, MemorySubject subject, ulong now, Span<RecalledMemory> results) =>
        Collect(mind, now, subject, results);

    /// <inheritdoc />
    public Opinion GetOpinionBaseline(MindHandle mind, MemorySubject subject)
    {
        EnsureValid(mind);
        int index = FindOpinion(mind, subject);
        if (index < 0)
        {
            return Opinion.Neutral;
        }

        Opinion tenths = _opinionBaselineTenths[index];
        return new Opinion(
            tenths.Trust / BaselineTenthsPerPoint,
            tenths.Fear / BaselineTenthsPerPoint,
            tenths.Affection / BaselineTenthsPerPoint,
            tenths.Respect / BaselineTenthsPerPoint);
    }

    /// <inheritdoc />
    public void SeedOpinionBaseline(MindHandle mind, MemorySubject subject, Opinion baseline)
    {
        EnsureValid(mind);
        int index = FindOpinion(mind, subject);
        if (index >= 0 && _opinionBaselineTenths[index] != Opinion.Neutral)
        {
            return;
        }

        if (index < 0)
        {
            index = AddOpinionSlot(mind, subject);
        }

        _opinionBaselineTenths[index] = new Opinion(
            Math.Clamp(baseline.Trust * BaselineTenthsPerPoint, -MaxBaselineTenths, MaxBaselineTenths),
            Math.Clamp(baseline.Fear * BaselineTenthsPerPoint, -MaxBaselineTenths, MaxBaselineTenths),
            Math.Clamp(baseline.Affection * BaselineTenthsPerPoint, -MaxBaselineTenths, MaxBaselineTenths),
            Math.Clamp(baseline.Respect * BaselineTenthsPerPoint, -MaxBaselineTenths, MaxBaselineTenths));
    }

    /// <inheritdoc />
    public int ShareRumours(MindHandle teller, MindHandle listener, ulong now, int maxShared)
    {
        EnsureValid(teller);
        EnsureValid(listener);
        ArgumentOutOfRangeException.ThrowIfNegative(maxShared);
        if (teller == listener || maxShared == 0)
        {
            return 0;
        }

        uint tick = ToRecordTick(now);
        int shared = 0;
        int previousStrength = int.MaxValue;
        int previousIndex = -1;
        while (shared < maxShared)
        {
            int best = StrongestFirstHandAboutPlayers(teller, tick, previousStrength, previousIndex, out int strength);
            if (best < 0)
            {
                break;
            }

            previousStrength = strength;
            previousIndex = best;
            if (ReceiveRumour(listener, _records[best], strength / 2, tick))
            {
                shared++;
            }
        }

        return shared;
    }

    /// <inheritdoc />
    public int GetNeed(MindHandle mind, Need need, ulong now)
    {
        EnsureValid(mind);
        return NeedMilli(mind, need, ToRecordTick(now)) / MilliPoints;
    }

    /// <inheritdoc />
    public void SatisfyNeed(MindHandle mind, Need need, int amount, ulong now)
    {
        EnsureValid(mind);
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        uint tick = ToRecordTick(now);
        int index = (mind.Slot * NeedCount) + (int)need;
        _needMilli[index] = Math.Max(0, NeedMilli(mind, need, tick) - (amount * MilliPoints));
        _needTicks[index] = tick;
    }

    /// <inheritdoc />
    public void SetNeed(MindHandle mind, Need need, int level, ulong now)
    {
        EnsureValid(mind);
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, ICreatureMindStore.MaxNeed);
        if ((uint)need >= NeedCount)
        {
            throw new ArgumentOutOfRangeException(nameof(need), need, "Unknown need.");
        }

        int index = (mind.Slot * NeedCount) + (int)need;
        _needMilli[index] = level * MilliPoints;
        _needTicks[index] = ToRecordTick(now);
    }

    /// <inheritdoc />
    public ReadOnlySpan<MemoryRecord> GetCoreRecords(MindHandle mind)
    {
        EnsureValid(mind);
        return new ReadOnlySpan<MemoryRecord>(_records, mind.Slot * SlotsPerMind, _coreCounts[mind.Slot]);
    }

    /// <inheritdoc />
    public ReadOnlySpan<MemoryRecord> GetEpisodicRecords(MindHandle mind)
    {
        EnsureValid(mind);
        return new ReadOnlySpan<MemoryRecord>(_records, (mind.Slot * SlotsPerMind) + ICreatureMindStore.CoreCapacity, _episodicCounts[mind.Slot]);
    }

    /// <summary>Copies everything saved about a mind (M1-17b).</summary>
    public MindState Export(MindHandle mind)
    {
        EnsureValid(mind);
        int slot = mind.Slot;
        int opinionStart = slot * ICreatureMindStore.OpinionCapacity;
        var baselines = new OpinionBaseline[_opinionCounts[slot]];
        for (int i = 0; i < baselines.Length; i++)
        {
            baselines[i] = new OpinionBaseline(_opinionSubjects[opinionStart + i], _opinionBaselineTenths[opinionStart + i]);
        }

        var needs = new NeedState[NeedCount];
        for (int i = 0; i < NeedCount; i++)
        {
            int index = (slot * NeedCount) + i;
            needs[i] = new NeedState(_needMilli[index], _needTicks[index], _needRates[index]);
        }

        return new MindState(
            _birth[slot], _current[slot], GetCoreRecords(mind).ToArray(), GetEpisodicRecords(mind).ToArray(), baselines, needs);
    }

    /// <summary>Allocates a mind with saved state (M1-17b); the inverse of <see cref="Export"/>.</summary>
    public MindHandle Import(MindState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.CoreRecords.Count > ICreatureMindStore.CoreCapacity || state.EpisodicRecords.Count > ICreatureMindStore.EpisodicCapacity
            || state.Baselines.Count > ICreatureMindStore.OpinionCapacity || state.Needs.Count != NeedCount)
        {
            throw new ArgumentException("Saved mind exceeds the store's capacities.", nameof(state));
        }

        MindHandle mind = Allocate(state.Birth, new NeedRates { Hunger = 0, Rest = 0, Safety = 0, Social = 0, Territory = 0 });
        int slot = mind.Slot;
        _current[slot] = state.Current;
        for (int i = 0; i < state.CoreRecords.Count; i++)
        {
            _records[(slot * SlotsPerMind) + i] = state.CoreRecords[i];
        }

        for (int i = 0; i < state.EpisodicRecords.Count; i++)
        {
            _records[(slot * SlotsPerMind) + ICreatureMindStore.CoreCapacity + i] = state.EpisodicRecords[i];
        }

        _coreCounts[slot] = state.CoreRecords.Count;
        _episodicCounts[slot] = state.EpisodicRecords.Count;
        int opinionStart = slot * ICreatureMindStore.OpinionCapacity;
        for (int i = 0; i < state.Baselines.Count; i++)
        {
            _opinionSubjects[opinionStart + i] = state.Baselines[i].Subject;
            _opinionBaselineTenths[opinionStart + i] = state.Baselines[i].Tenths;
        }

        _opinionCounts[slot] = state.Baselines.Count;
        for (int i = 0; i < NeedCount; i++)
        {
            int index = (slot * NeedCount) + i;
            (_needMilli[index], _needTicks[index], _needRates[index]) = (state.Needs[i].Milli, state.Needs[i].Tick, state.Needs[i].Rate);
        }

        return mind;
    }

    /// <summary>Total records stored across all live minds (core and episodic, including forgotten ones not yet replaced).</summary>
    public int TotalRecords
    {
        get
        {
            int total = 0;
            for (int slot = 0; slot < _nextUnusedSlot; slot++)
            {
                if (_alive[slot])
                {
                    total += _coreCounts[slot] + _episodicCounts[slot];
                }
            }

            return total;
        }
    }

    /// <summary>Adds every live mind's personality and records in use to a determinism hash (TDD §2.3).</summary>
    public void AddTo(ref StateHash hash)
    {
        for (int slot = 0; slot < _nextUnusedSlot; slot++)
        {
            if (!_alive[slot])
            {
                continue;
            }

            hash.Add((ulong)slot);
            hash.Add<Personality>(new ReadOnlySpan<Personality>(in _current[slot]));
            hash.Add<Opinion>(new ReadOnlySpan<Opinion>(_opinionBaselineTenths, slot * ICreatureMindStore.OpinionCapacity, _opinionCounts[slot]));
            for (int i = slot * ICreatureMindStore.OpinionCapacity; i < (slot * ICreatureMindStore.OpinionCapacity) + _opinionCounts[slot]; i++)
            {
                // Field by field: MemorySubject has padding bytes whose content is undefined.
                hash.Add((ulong)_opinionSubjects[i].Kind);
                hash.Add(_opinionSubjects[i].Id);
            }

            hash.Add<int>(new ReadOnlySpan<int>(_needMilli, slot * NeedCount, NeedCount));
            hash.Add<uint>(new ReadOnlySpan<uint>(_needTicks, slot * NeedCount, NeedCount));
            hash.Add((ulong)_coreCounts[slot]);
            AddRecords(ref hash, slot * SlotsPerMind, _coreCounts[slot]);
            hash.Add((ulong)_episodicCounts[slot]);
            AddRecords(ref hash, (slot * SlotsPerMind) + ICreatureMindStore.CoreCapacity, _episodicCounts[slot]);
        }
    }

    // Field by field: MemoryRecord has 7 padding bytes whose content is undefined, so its raw bytes must not be hashed.
    private void AddRecords(ref StateHash hash, int start, int count)
    {
        for (int i = start; i < start + count; i++)
        {
            MemoryRecord record = _records[i];
            hash.Add(record.SubjectId);
            hash.Add(((ulong)record.CreatedTick << 32) | record.LastReinforcedTick);
            hash.Add(((ulong)record.EventType << 48) | ((ulong)record.Salience << 32) | ((ulong)record.Count << 16) | (ulong)record.SubjectKind);
            hash.Add(((ulong)(byte)record.Valence << 8) | (ulong)record.Tags);
        }
    }

    private static uint ToRecordTick(ulong now) =>
        now <= uint.MaxValue
            ? (uint)now
            : throw new ArgumentOutOfRangeException(nameof(now), now, "World tick exceeds the memory record range (about 4.5 years at 30 Hz).");

    private int NeedMilli(MindHandle mind, Need need, uint tick)
    {
        if ((uint)need >= NeedCount)
        {
            throw new ArgumentOutOfRangeException(nameof(need), need, "Unknown need.");
        }

        int index = (mind.Slot * NeedCount) + (int)need;
        if (tick < _needTicks[index])
        {
            throw new ArgumentOutOfRangeException(nameof(tick), tick, $"Time went backwards: need last set at tick {_needTicks[index]}.");
        }

        long risen = (long)_needRates[index] * MilliPoints * (tick - _needTicks[index]) / _rules.TicksPerDay;
        return (int)Math.Min(ICreatureMindStore.MaxNeed * MilliPoints, _needMilli[index] + risen);
    }

    // The long-term baseline moves by 10% of the episode's contribution: contribution points == baseline tenths (AI Bible §5).
    private void AdjustBaseline(MindHandle mind, MemorySubject subject, Opinion weights, int baseSalience)
    {
        if (weights == Opinion.Neutral)
        {
            return;
        }

        var delta = new Opinion(
            weights.Trust * baseSalience / ICreatureMindStore.MaxSalience,
            weights.Fear * baseSalience / ICreatureMindStore.MaxSalience,
            weights.Affection * baseSalience / ICreatureMindStore.MaxSalience,
            weights.Respect * baseSalience / ICreatureMindStore.MaxSalience);

        int index = FindOpinion(mind, subject);
        if (index < 0)
        {
            index = AddOpinionSlot(mind, subject);
        }

        Opinion current = _opinionBaselineTenths[index];
        _opinionBaselineTenths[index] = new Opinion(
            Math.Clamp(current.Trust + delta.Trust, -MaxBaselineTenths, MaxBaselineTenths),
            Math.Clamp(current.Fear + delta.Fear, -MaxBaselineTenths, MaxBaselineTenths),
            Math.Clamp(current.Affection + delta.Affection, -MaxBaselineTenths, MaxBaselineTenths),
            Math.Clamp(current.Respect + delta.Respect, -MaxBaselineTenths, MaxBaselineTenths));
    }

    private int FindOpinion(MindHandle mind, MemorySubject subject)
    {
        int start = mind.Slot * ICreatureMindStore.OpinionCapacity;
        for (int i = start; i < start + _opinionCounts[mind.Slot]; i++)
        {
            if (_opinionSubjects[i] == subject)
            {
                return i;
            }
        }

        return -1;
    }

    // Full: replace the subject the creature feels least strongly about (lowest slot on ties). Owner protection: M1-11.
    private int AddOpinionSlot(MindHandle mind, MemorySubject subject)
    {
        int start = mind.Slot * ICreatureMindStore.OpinionCapacity;
        int index;
        if (_opinionCounts[mind.Slot] < ICreatureMindStore.OpinionCapacity)
        {
            index = start + _opinionCounts[mind.Slot]++;
        }
        else
        {
            index = start;
            for (int i = start + 1; i < start + ICreatureMindStore.OpinionCapacity; i++)
            {
                if (_opinionBaselineTenths[i].Intensity < _opinionBaselineTenths[index].Intensity)
                {
                    index = i;
                }
            }
        }

        _opinionSubjects[index] = subject;
        _opinionBaselineTenths[index] = Opinion.Neutral;
        return index;
    }

    // Next strongest first-hand (non-rumour), remembered memory about a player, strictly after (previousStrength, previousIndex)
    // in "strongest first, then lowest index" order. No allocation: scans the creature's slots.
    private int StrongestFirstHandAboutPlayers(MindHandle mind, uint tick, int previousStrength, int previousIndex, out int strength)
    {
        int start = mind.Slot * SlotsPerMind;
        int best = -1;
        strength = -1;
        for (int i = start; i < start + SlotsPerMind; i++)
        {
            bool used = i < start + _coreCounts[mind.Slot]
                || (i >= start + ICreatureMindStore.CoreCapacity && i < start + ICreatureMindStore.CoreCapacity + _episodicCounts[mind.Slot]);
            if (!used || _records[i].SubjectKind != SubjectKind.Player || (_records[i].Tags & MemoryTags.Rumour) != 0)
            {
                continue;
            }

            int value = Effective(_records[i], tick);
            bool afterPrevious = value < previousStrength || (value == previousStrength && i > previousIndex);
            if (value >= ICreatureMindStore.ForgetThreshold && afterPrevious && value > strength)
            {
                best = i;
                strength = value;
            }
        }

        return best;
    }

    // Removes an episodic record by moving the last one into its slot (deterministic order).
    private void RemoveEpisodic(MindHandle mind, int index)
    {
        int last = (mind.Slot * SlotsPerMind) + ICreatureMindStore.CoreCapacity + _episodicCounts[mind.Slot] - 1;
        _records[index] = _records[last];
        _records[last] = default;
        _episodicCounts[mind.Slot]--;
    }

    // A rumour never overrides first-hand experience; it only refreshes a weaker rumour of the same event and subject.
    private bool ReceiveRumour(MindHandle to, in MemoryRecord heard, int salience, uint tick)
    {
        if (salience < ICreatureMindStore.ForgetThreshold)
        {
            return false;
        }

        var rumour = new MemoryRecord(heard.EventType, heard.Subject, heard.Valence, (ushort)salience, tick, tick, 1, MemoryTags.Rumour);
        int start = (to.Slot * SlotsPerMind) + ICreatureMindStore.CoreCapacity;
        int existing = FindIndex(to.Slot * SlotsPerMind, _coreCounts[to.Slot], heard.EventType, heard.Subject);
        if (existing < 0)
        {
            existing = FindIndex(start, _episodicCounts[to.Slot], heard.EventType, heard.Subject);
        }

        if (existing >= 0)
        {
            MemoryRecord current = _records[existing];
            bool currentIsRumour = (current.Tags & MemoryTags.Rumour) != 0;
            if (!currentIsRumour || Effective(current, tick) >= salience)
            {
                return false;
            }

            _records[existing] = rumour;
            return true;
        }

        AddEpisodic(to, rumour, tick);
        return true;
    }

    private int FindIndex(int start, int count, ushort eventType, MemorySubject subject)
    {
        for (int i = start; i < start + count; i++)
        {
            if (_records[i].EventType == eventType && _records[i].SubjectKind == subject.Kind && _records[i].SubjectId == subject.Id)
            {
                return i;
            }
        }

        return -1;
    }

    private int Effective(in MemoryRecord record, uint tick)
    {
        if (tick < record.LastReinforcedTick)
        {
            throw new ArgumentOutOfRangeException(nameof(tick), tick, $"Time went backwards: memory last reinforced at tick {record.LastReinforcedTick}.");
        }

        int value = MemoryDecay.Apply(record.Salience, tick - record.LastReinforcedTick, _rules.HalfLifeTicks(record.EventType));
        return record.IsPinned ? Math.Max(value, Math.Min((int)record.Salience, ICreatureMindStore.PinnedFloor)) : value;
    }

    private MemoryRecord Reinforce(in MemoryRecord record, MemoryEventTypeDefinition definition, uint tick)
    {
        int salience = Math.Min(
            ICreatureMindStore.MaxSalience,
            Effective(record, tick) + (definition.BaseSalience * ReinforceNumerator / ReinforceDenominator));
        ushort count = record.Count == ushort.MaxValue ? ushort.MaxValue : (ushort)(record.Count + 1);

        return new MemoryRecord(
            record.EventType, record.Subject, (sbyte)definition.Valence, (ushort)salience, record.CreatedTick, tick, count, record.Tags);
    }

    private bool TryPin(MindHandle mind, MemoryRecord record, uint tick, out bool evicted)
    {
        int start = mind.Slot * SlotsPerMind;
        var pinned = record with { Tags = record.Tags | MemoryTags.Pinned };
        evicted = false;

        if (_coreCounts[mind.Slot] < ICreatureMindStore.CoreCapacity)
        {
            _records[start + _coreCounts[mind.Slot]++] = pinned;
            return true;
        }

        int weakest = FindWeakest(start, ICreatureMindStore.CoreCapacity, tick, out int weakestStrength);
        if (record.Salience <= weakestStrength)
        {
            return false;
        }

        // The displaced core memory is not lost: it continues as an ordinary episodic memory.
        MemoryRecord displaced = _records[weakest];
        _records[weakest] = pinned;
        evicted = AddEpisodic(mind, displaced with { Tags = displaced.Tags & ~MemoryTags.Pinned }, tick);
        return true;
    }

    private bool AddEpisodic(MindHandle mind, in MemoryRecord record, uint tick)
    {
        int start = (mind.Slot * SlotsPerMind) + ICreatureMindStore.CoreCapacity;
        if (_episodicCounts[mind.Slot] < ICreatureMindStore.EpisodicCapacity)
        {
            _records[start + _episodicCounts[mind.Slot]++] = record;
            return false;
        }

        int weakest = FindWeakest(start, ICreatureMindStore.EpisodicCapacity, tick, out _);
        _records[weakest] = record;
        return true;
    }

    // Weakest by strength now; ties go to the least recently reinforced, then the lowest slot (deterministic).
    private int FindWeakest(int start, int count, uint tick, out int strength)
    {
        int weakest = start;
        strength = Effective(_records[start], tick);
        for (int i = start + 1; i < start + count; i++)
        {
            int value = Effective(_records[i], tick);
            if (value < strength || (value == strength && _records[i].LastReinforcedTick < _records[weakest].LastReinforcedTick))
            {
                weakest = i;
                strength = value;
            }
        }

        return weakest;
    }

    private PersonalityTrait? ApplyDrift(MindHandle mind, MemoryEventTypeDefinition definition)
    {
        if (definition.TraitDrift is not TraitDrift drift)
        {
            return null;
        }

        _current[mind.Slot] = _current[mind.Slot].Drift(drift.Trait, drift.Amount, _birth[mind.Slot]);
        return drift.Trait;
    }

    private int Collect(MindHandle mind, ulong now, MemorySubject? subject, Span<RecalledMemory> results)
    {
        EnsureValid(mind);
        uint tick = ToRecordTick(now);
        int start = mind.Slot * SlotsPerMind;
        int found = 0;
        found = CollectRange(start, _coreCounts[mind.Slot], tick, subject, results, found);
        found = CollectRange(start + ICreatureMindStore.CoreCapacity, _episodicCounts[mind.Slot], tick, subject, results, found);
        return found;
    }

    private int CollectRange(int start, int count, uint tick, MemorySubject? subject, Span<RecalledMemory> results, int found)
    {
        for (int i = start; i < start + count; i++)
        {
            MemoryRecord record = _records[i];
            if (subject is MemorySubject wanted && record.Subject != wanted)
            {
                continue;
            }

            int strength = Effective(record, tick);
            if (strength < ICreatureMindStore.ForgetThreshold && !record.IsPinned)
            {
                continue;
            }

            found = InsertSorted(results, found, new RecalledMemory(record, strength));
        }

        return found;
    }

    // Keeps results sorted strongest first; when full, the weakest falls off the end.
    private static int InsertSorted(Span<RecalledMemory> results, int count, RecalledMemory item)
    {
        int position = count;
        while (position > 0 && IsStronger(item, results[position - 1]))
        {
            position--;
        }

        if (position >= results.Length)
        {
            return count;
        }

        int last = Math.Min(count, results.Length - 1);
        for (int i = last; i > position; i--)
        {
            results[i] = results[i - 1];
        }

        results[position] = item;
        return Math.Min(count + 1, results.Length);
    }

    private static bool IsStronger(in RecalledMemory a, in RecalledMemory b)
    {
        if (a.EffectiveSalience != b.EffectiveSalience)
        {
            return a.EffectiveSalience > b.EffectiveSalience;
        }

        if (a.Record.CreatedTick != b.Record.CreatedTick)
        {
            return a.Record.CreatedTick < b.Record.CreatedTick;
        }

        return a.Record.EventType < b.Record.EventType
            || (a.Record.EventType == b.Record.EventType && a.Record.SubjectId < b.Record.SubjectId);
    }

    private void EnsureValid(MindHandle mind)
    {
        if (!IsValid(mind))
        {
            throw new InvalidOperationException($"{mind} is not a live creature mind.");
        }
    }
}
