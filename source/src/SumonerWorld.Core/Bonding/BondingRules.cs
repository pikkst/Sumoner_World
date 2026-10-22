using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Cognition.Opinions;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Diagnostics;

namespace SumonerWorld.Core.Bonding;

/// <summary>Ownership states (AI Bible §10.1). Released and Left return a creature to <see cref="Wild"/>.</summary>
public enum OwnershipState : byte
{
    /// <summary>No owner.</summary>
    Wild,

    /// <summary>Bonded to a player and travelling with them.</summary>
    Pacted,

    /// <summary>Bonded and living at the owner's Sanctum.</summary>
    SanctumResident,

    /// <summary>Died on a Normal server; can be revived.</summary>
    Fallen,

    /// <summary>Died permanently (Hardcore preset).</summary>
    Deceased,
}

/// <summary>Result of a bonding operation. Anything other than success leaves the state unchanged.</summary>
public enum BondingOutcome : byte
{
    /// <summary>Done.</summary>
    Ok,

    /// <summary>The creature accepted the Pact.</summary>
    Accepted,

    /// <summary>The creature refused the Pact (remembered as <c>PactRefused</c>).</summary>
    Refused,

    /// <summary>The caller is not the owner.</summary>
    NotOwner,

    /// <summary>Not allowed in the creature's current state.</summary>
    WrongState,

    /// <summary>Summoning would exceed the owner's Bond Capacity.</summary>
    CapacityReached,

    /// <summary>The creature left its owner (broken terms or lasting distrust) and is wild again.</summary>
    Left,

    /// <summary>The creature left and turned hostile (Temper above the hostility threshold).</summary>
    LeftHostile,

    /// <summary>Nothing changed.</summary>
    Unchanged,

    /// <summary>The creature belongs to another summoner class and refused (companions spec §2; remembered as <c>PactRefused</c>).</summary>
    WrongClass,
}

/// <summary>A creature's bond (saved, AI Bible §10).</summary>
/// <param name="State">Ownership state.</param>
/// <param name="Owner">Owner account id (0 when wild).</param>
/// <param name="Terms">Agreed Pact Terms.</param>
/// <param name="BrokenTerms">Terms broken so far under this Pact.</param>
/// <param name="BondLevel">Current bond level (AI Bible §10.3).</param>
/// <param name="Summoned">True while summoned at the owner's side (always AI LOD T0).</param>
/// <param name="PactTick">Tick the Pact was sealed.</param>
/// <param name="DistrustSinceTick">Tick Trust first fell below the leave threshold, if <paramref name="Distrustful"/>.</param>
/// <param name="Distrustful">True while Trust stays below the leave threshold.</param>
public readonly record struct BondState(
    OwnershipState State, ulong Owner, PactTerms Terms, byte BrokenTerms, byte BondLevel, bool Summoned, uint PactTick, uint DistrustSinceTick, bool Distrustful);

/// <summary>
/// The Bonding Contract v1.0.0 (AI Bible §10, R29): every creature follows these ownership, Pact, bond-level and summoning rules.
/// Server-only. Each change of owner is one atomic operation here; in multiplayer it runs inside the transaction processor (M1-15,
/// R27, R34).
/// </summary>
public interface IBondingRules
{
    /// <summary>The contract version (semantic versioning; a change needs an ADR and a save migration).</summary>
    string ContractVersion { get; }

    /// <summary>The creature's bond.</summary>
    BondState GetBond(MindHandle creature);

    /// <summary>
    /// A player offers a Pact to a wild creature. Writes <c>PactOffered</c>, then <c>PactSealed</c> or <c>PactRefused</c>. With
    /// <paramref name="conditions"/>, a creature of another class always refuses (<see cref="BondingOutcome.WrongClass"/>) and the
    /// level difference counts (companions spec §2).
    /// </summary>
    BondingOutcome OfferPact(
        MindHandle creature, SpeciesDefinition species, MemorySubject player, PactTerms terms, int reputation, ulong now, out PactDecision decision,
        Progression.PactConditions? conditions = null);

    /// <summary>
    /// The starter Pact at the Academy (Academy spec §4, M2-22): the creature the Keeper brought for this student always accepts.
    /// It is still a real bond: the offer, the sealed Pact and a <c>FirstBond</c> core memory are written as for any Pact.
    /// </summary>
    BondingOutcome SealStarterPact(MindHandle creature, MemorySubject player, PactTerms terms, ulong now);

    /// <summary>
    /// The owner hands the creature to another player; the creature must accept the new player like a Pact (a player of another
    /// class never). Memories travel with it.
    /// </summary>
    BondingOutcome Repact(
        MindHandle creature, SpeciesDefinition species, MemorySubject owner, MemorySubject newOwner, PactTerms terms, int reputation, ulong now, out PactDecision decision,
        Progression.PactConditions? conditions = null);

    /// <summary>The owner releases the creature (writes <c>ReleasedBy</c>).</summary>
    BondingOutcome Release(MindHandle creature, MemorySubject owner, ulong now);

    /// <summary>Daily server check of the Pact Terms: kept terms write <c>PactTermKept</c>, broken ones <c>PactBroken</c>; the third broken term makes it leave.</summary>
    BondingOutcome CheckTerms(MindHandle creature, PactTerms keptTerms, ulong now);

    /// <summary>Daily update: leaves after Trust below −50 for 3 in-game days; recalculates the bond level.</summary>
    BondingOutcome DailyUpdate(MindHandle creature, ulong now);

    /// <summary>Recalculates the bond level now (also call right after a core memory about the owner forms).</summary>
    int RecalculateBondLevel(MindHandle creature, ulong now);

    /// <summary>Summons the creature to its owner's side if Bond Capacity allows (AI Bible §10.4).</summary>
    BondingOutcome Summon(MindHandle creature, MemorySubject owner, int activeSummons, int bondCapacity);

    /// <summary>Sends a summoned creature back.</summary>
    BondingOutcome Dismiss(MindHandle creature, MemorySubject owner);

    /// <summary>Moves a Pacted creature to the owner's Sanctum, or back (<paramref name="toSanctum"/> false).</summary>
    BondingOutcome SetSanctum(MindHandle creature, MemorySubject owner, bool toSanctum);

    /// <summary>The creature died: Deceased if <paramref name="permanent"/> (Hardcore), otherwise Fallen.</summary>
    BondingOutcome Die(MindHandle creature, bool permanent);

    /// <summary>The owner revives a Fallen creature (Normal servers).</summary>
    BondingOutcome Revive(MindHandle creature, MemorySubject owner);
}

/// <summary><see cref="IBondingRules"/> v1.0.0. Bond state is kept per mind slot (preallocated, R21).</summary>
public sealed class BondingRules : IBondingRules
{
    /// <summary>Bonding Contract version implemented (AI Bible §10).</summary>
    public const string Version = "1.0.0";

    /// <summary>Trust below this, for <see cref="DistrustDaysToLeave"/> days, makes a creature leave (AI Bible §10.1).</summary>
    public const int LeaveTrustBelow = -50;

    /// <summary>In-game days of distrust before leaving.</summary>
    public const int DistrustDaysToLeave = 3;

    /// <summary>Broken terms that make a creature leave.</summary>
    public const int BrokenTermsToLeave = 3;

    /// <summary>Creatures with Temper above this turn hostile when they leave.</summary>
    public const int HostileTemperAbove = 40;

    /// <summary>Largest Bond Capacity at EA (AI Bible §10.4).</summary>
    public const int MaxBondCapacity = 5;

    private readonly ICreatureMindStore _store;
    private readonly IOpinionModel _opinions;
    private readonly IPactEvaluator _evaluator;
    private readonly IReadOnlyList<BondLevelDefinition> _levels;
    private readonly int _termCount;
    private readonly uint _ticksPerDay;
    private readonly BondState[] _states;
    private readonly int[] _owners;
    private readonly RecalledMemory[] _recalled = new RecalledMemory[ICreatureMindStore.CoreCapacity + ICreatureMindStore.EpisodicCapacity];
    private readonly ushort _pactOffered;
    private readonly ushort _pactSealed;
    private readonly ushort? _firstBond;
    private readonly ushort _pactRefused;
    private readonly ushort _pactTermKept;
    private readonly ushort _pactBroken;
    private readonly ushort _releasedBy;

    /// <summary>Creates the rules for every mind slot of <paramref name="store"/>.</summary>
    public BondingRules(
        ICreatureMindStore store,
        IOpinionModel opinions,
        IPactEvaluator evaluator,
        MemoryRules rules,
        IReadOnlyList<BondLevelDefinition> levels,
        int termCount,
        int ticksPerDay)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(opinions);
        ArgumentNullException.ThrowIfNull(evaluator);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(levels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ticksPerDay);
        if (levels.Count == 0 || levels[0].Level != 0)
        {
            throw new ArgumentException("Bond levels must start at level 0.", nameof(levels));
        }

        _store = store;
        _opinions = opinions;
        _evaluator = evaluator;
        _levels = levels;
        _termCount = termCount;
        _ticksPerDay = (uint)ticksPerDay;
        _states = new BondState[store.Capacity];
        _owners = new int[store.Capacity];
        Array.Fill(_owners, -1);
        _pactOffered = Require(rules, "PactOffered");
        _pactSealed = Require(rules, "PactSealed");
        _pactRefused = Require(rules, "PactRefused");
        _pactTermKept = Require(rules, "PactTermKept");
        _pactBroken = Require(rules, "PactBroken");
        _releasedBy = Require(rules, "ReleasedBy");
        _firstBond = rules.FindByKey("FirstBond");
    }

    /// <inheritdoc />
    public string ContractVersion => Version;

    /// <inheritdoc />
    public BondState GetBond(MindHandle creature) => State(creature);

    /// <inheritdoc />
    public BondingOutcome OfferPact(
        MindHandle creature, SpeciesDefinition species, MemorySubject player, PactTerms terms, int reputation, ulong now, out PactDecision decision,
        Progression.PactConditions? conditions = null)
    {
        RequirePlayer(player);
        decision = default;
        if (State(creature).State != OwnershipState.Wild)
        {
            return BondingOutcome.WrongState;
        }

        _store.Remember(creature, _pactOffered, player, now);
        decision = _evaluator.Evaluate(creature, species, player, terms, reputation, now, conditions);
        if (!decision.Accepted)
        {
            _store.Remember(creature, _pactRefused, player, now);
            return decision.WrongClass ? BondingOutcome.WrongClass : BondingOutcome.Refused;
        }

        Seal(creature, player, terms, now);
        return BondingOutcome.Accepted;
    }

    /// <inheritdoc />
    public BondingOutcome SealStarterPact(MindHandle creature, MemorySubject player, PactTerms terms, ulong now)
    {
        RequirePlayer(player);
        if (State(creature).State != OwnershipState.Wild)
        {
            return BondingOutcome.WrongState;
        }

        _store.Remember(creature, _pactOffered, player, now);
        Seal(creature, player, terms, now);
        if (_firstBond is ushort firstBond)
        {
            _store.Remember(creature, firstBond, player, now);
            RecalculateBondLevel(creature, now);
        }

        return BondingOutcome.Accepted;
    }

    /// <inheritdoc />
    public BondingOutcome Repact(
        MindHandle creature, SpeciesDefinition species, MemorySubject owner, MemorySubject newOwner, PactTerms terms, int reputation, ulong now, out PactDecision decision,
        Progression.PactConditions? conditions = null)
    {
        RequirePlayer(newOwner);
        decision = default;
        BondingOutcome check = CheckOwner(creature, owner, OwnershipState.Pacted);
        if (check != BondingOutcome.Ok)
        {
            return check;
        }

        decision = _evaluator.Evaluate(creature, species, newOwner, terms, reputation, now, conditions);
        if (!decision.Accepted)
        {
            _store.Remember(creature, _pactRefused, newOwner, now);
            return decision.WrongClass ? BondingOutcome.WrongClass : BondingOutcome.Refused;
        }

        Seal(creature, newOwner, terms, now);
        return BondingOutcome.Accepted;
    }

    /// <inheritdoc />
    public BondingOutcome Release(MindHandle creature, MemorySubject owner, ulong now)
    {
        BondState state = State(creature);
        BondingOutcome check = CheckOwner(creature, owner, state.State == OwnershipState.SanctumResident ? OwnershipState.SanctumResident : OwnershipState.Pacted);
        if (check != BondingOutcome.Ok)
        {
            return check;
        }

        _store.Remember(creature, _releasedBy, owner, now);
        Set(creature, default);
        return BondingOutcome.Ok;
    }

    /// <inheritdoc />
    public BondingOutcome CheckTerms(MindHandle creature, PactTerms keptTerms, ulong now)
    {
        BondState state = State(creature);
        if (state.State is not (OwnershipState.Pacted or OwnershipState.SanctumResident))
        {
            return BondingOutcome.WrongState;
        }

        var owner = new MemorySubject(SubjectKind.Player, state.Owner);
        int broken = state.BrokenTerms;
        bool brokeAny = false;
        for (int i = 0; i < _termCount; i++)
        {
            if (!state.Terms.Contains(i))
            {
                continue;
            }

            bool kept = keptTerms.Contains(i);
            _store.Remember(creature, kept ? _pactTermKept : _pactBroken, owner, now);
            broken += kept ? 0 : 1;
            brokeAny |= !kept;
        }

        Set(creature, State(creature) with { BrokenTerms = (byte)Math.Min(broken, byte.MaxValue) });
        if (broken >= BrokenTermsToLeave)
        {
            return Leave(creature);
        }

        if (brokeAny)
        {
            RecalculateBondLevel(creature, now);
        }

        return BondingOutcome.Ok;
    }

    /// <inheritdoc />
    public BondingOutcome DailyUpdate(MindHandle creature, ulong now)
    {
        BondState state = State(creature);
        if (state.State is not (OwnershipState.Pacted or OwnershipState.SanctumResident))
        {
            return BondingOutcome.Unchanged;
        }

        uint tick = ToTick(now);
        int trust = _opinions.GetOpinion(creature, new MemorySubject(SubjectKind.Player, state.Owner), now).Trust;
        if (trust >= LeaveTrustBelow)
        {
            Set(creature, state with { Distrustful = false, DistrustSinceTick = 0 });
        }
        else if (!state.Distrustful)
        {
            Set(creature, state with { Distrustful = true, DistrustSinceTick = tick });
        }
        else if (tick - state.DistrustSinceTick >= DistrustDaysToLeave * _ticksPerDay)
        {
            _store.Remember(creature, _pactBroken, new MemorySubject(SubjectKind.Player, state.Owner), now);
            return Leave(creature);
        }

        RecalculateBondLevel(creature, now);
        return BondingOutcome.Ok;
    }

    /// <inheritdoc />
    public int RecalculateBondLevel(MindHandle creature, ulong now)
    {
        BondState state = State(creature);
        if (state.State is not (OwnershipState.Pacted or OwnershipState.SanctumResident))
        {
            return 0;
        }

        var owner = new MemorySubject(SubjectKind.Player, state.Owner);
        Opinion opinion = _opinions.GetOpinion(creature, owner, now);
        int coreMemories = CountCoreMemoriesAbout(creature, owner, now);
        uint days = (ToTick(now) - state.PactTick) / _ticksPerDay;
        int level = 0;
        for (int i = 0; i < _levels.Count; i++)
        {
            BondLevelDefinition definition = _levels[i];
            if (opinion.Trust >= definition.MinTrust && opinion.Affection >= definition.MinAffection
                && coreMemories >= definition.CoreMemoriesWithOwner && days >= definition.MinDaysBonded)
            {
                level = definition.Level;
            }
        }

        Set(creature, state with { BondLevel = (byte)level });
        return level;
    }

    /// <inheritdoc />
    public BondingOutcome Summon(MindHandle creature, MemorySubject owner, int activeSummons, int bondCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(activeSummons);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bondCapacity);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bondCapacity, MaxBondCapacity);

        BondingOutcome check = CheckOwner(creature, owner, OwnershipState.Pacted);
        if (check != BondingOutcome.Ok)
        {
            return check;
        }

        BondState state = State(creature);
        if (state.Summoned)
        {
            return BondingOutcome.Unchanged;
        }

        if (activeSummons >= bondCapacity)
        {
            return BondingOutcome.CapacityReached;
        }

        Set(creature, state with { Summoned = true });
        return BondingOutcome.Ok;
    }

    /// <inheritdoc />
    public BondingOutcome Dismiss(MindHandle creature, MemorySubject owner)
    {
        BondingOutcome check = CheckOwner(creature, owner, OwnershipState.Pacted);
        if (check != BondingOutcome.Ok)
        {
            return check;
        }

        Set(creature, State(creature) with { Summoned = false });
        return BondingOutcome.Ok;
    }

    /// <inheritdoc />
    public BondingOutcome SetSanctum(MindHandle creature, MemorySubject owner, bool toSanctum)
    {
        OwnershipState from = toSanctum ? OwnershipState.Pacted : OwnershipState.SanctumResident;
        BondingOutcome check = CheckOwner(creature, owner, from);
        if (check != BondingOutcome.Ok)
        {
            return check;
        }

        Set(creature, State(creature) with { State = toSanctum ? OwnershipState.SanctumResident : OwnershipState.Pacted, Summoned = false });
        return BondingOutcome.Ok;
    }

    /// <inheritdoc />
    public BondingOutcome Die(MindHandle creature, bool permanent)
    {
        BondState state = State(creature);
        if (state.State is OwnershipState.Fallen or OwnershipState.Deceased)
        {
            return BondingOutcome.WrongState;
        }

        Set(creature, state with { State = permanent ? OwnershipState.Deceased : OwnershipState.Fallen, Summoned = false });
        return BondingOutcome.Ok;
    }

    /// <inheritdoc />
    public BondingOutcome Revive(MindHandle creature, MemorySubject owner)
    {
        BondingOutcome check = CheckOwner(creature, owner, OwnershipState.Fallen);
        if (check != BondingOutcome.Ok)
        {
            return check;
        }

        Set(creature, State(creature) with { State = OwnershipState.Pacted });
        return BondingOutcome.Ok;
    }

    /// <summary>Adds every live creature's bond to a determinism hash, field by field (BondState has padding).</summary>
    public void AddTo(ref StateHash hash)
    {
        for (int slot = 0; slot < _states.Length; slot++)
        {
            if (_owners[slot] < 0)
            {
                continue;
            }

            BondState state = _states[slot];
            hash.Add((ulong)slot);
            hash.Add(state.Owner);
            hash.Add(((ulong)state.State << 40) | ((ulong)state.Terms.Mask << 32) | ((ulong)state.BrokenTerms << 24)
                | ((ulong)state.BondLevel << 16) | (state.Summoned ? 1UL : 0) | (state.Distrustful ? 2UL : 0));
            hash.Add(((ulong)state.PactTick << 32) | state.DistrustSinceTick);
        }
    }

    private static ushort Require(MemoryRules rules, string key) =>
        rules.FindByKey(key) ?? throw new ArgumentException($"Bonding needs the memory event type '{key}' in the data.", nameof(rules));

    private static void RequirePlayer(MemorySubject player)
    {
        if (player.Kind != SubjectKind.Player || player.Id == 0)
        {
            throw new ArgumentException("Pacts are made with players (account id > 0).", nameof(player));
        }
    }

    private static uint ToTick(ulong now) =>
        now <= uint.MaxValue ? (uint)now : throw new ArgumentOutOfRangeException(nameof(now), now, "World tick exceeds the bond tick range.");

    private void Seal(MindHandle creature, MemorySubject player, PactTerms terms, ulong now)
    {
        Set(creature, new BondState(OwnershipState.Pacted, player.Id, terms, 0, 0, false, ToTick(now), 0, false));
        _store.Remember(creature, _pactSealed, player, now);
        RecalculateBondLevel(creature, now);
    }

    private BondingOutcome Leave(MindHandle creature)
    {
        bool hostile = _store.GetPersonality(creature).Temper > HostileTemperAbove;
        Set(creature, default);
        return hostile ? BondingOutcome.LeftHostile : BondingOutcome.Left;
    }

    private BondingOutcome CheckOwner(MindHandle creature, MemorySubject owner, OwnershipState required)
    {
        BondState state = State(creature);
        if (state.State != required)
        {
            return BondingOutcome.WrongState;
        }

        return owner.Kind == SubjectKind.Player && owner.Id == state.Owner ? BondingOutcome.Ok : BondingOutcome.NotOwner;
    }

    private int CountCoreMemoriesAbout(MindHandle creature, MemorySubject owner, ulong now)
    {
        int count = _store.RecallAbout(creature, owner, now, _recalled);
        int core = 0;
        for (int i = 0; i < count; i++)
        {
            core += (_recalled[i].Record.Tags & MemoryTags.Core) != 0 ? 1 : 0;
        }

        return core;
    }

    /// <summary>Sets a loaded creature's saved bond (M1-17b); not a gameplay transition.</summary>
    public void Restore(MindHandle creature, BondState state)
    {
        if (!_store.IsValid(creature))
        {
            throw new InvalidOperationException($"{creature} is not a live creature mind.");
        }

        Set(creature, state);
    }

    // A slot reused by a new creature starts wild.
    private BondState State(MindHandle creature)
    {
        if (!_store.IsValid(creature))
        {
            throw new InvalidOperationException($"{creature} is not a live creature mind.");
        }

        return _owners[creature.Slot] == creature.Generation ? _states[creature.Slot] : default;
    }

    private void Set(MindHandle creature, BondState state)
    {
        _owners[creature.Slot] = creature.Generation;
        _states[creature.Slot] = state;
    }
}
