namespace SumonerWorld.Core.Intents;

/// <summary>
/// Identifies one state-changing intent (TDD §3.4): the client's session plus a sequence that increases by one per intent. The
/// session id is assigned by the server (it is not the secret resume token).
/// </summary>
/// <param name="Session">Server-assigned session id.</param>
/// <param name="Sequence">Monotonic per session.</param>
public readonly record struct IntentId(ulong Session, uint Sequence);

/// <summary>Whether an intent changed the world.</summary>
public enum IntentStatus : byte
{
    /// <summary>The effect was applied (a refused Pact is applied too: the creature remembers being asked).</summary>
    Applied,

    /// <summary>Nothing changed; see <see cref="IntentResult.Reason"/>.</summary>
    Rejected,
}

/// <summary>Why an intent was rejected. Values are sent to clients: never reuse or renumber them.</summary>
public enum IntentRejectReason : byte
{
    /// <summary>Not rejected.</summary>
    None = 0,

    /// <summary>The id is older than the dedupe window or lower than one already seen; it is never applied.</summary>
    Stale = 1,

    /// <summary>The target does not exist (any more).</summary>
    InvalidTarget = 2,

    /// <summary>The arguments are out of range.</summary>
    InvalidArguments = 3,

    /// <summary>The target is too far away.</summary>
    OutOfRange = 4,

    /// <summary>The action is still cooling down.</summary>
    OnCooldown = 5,

    /// <summary>Not allowed in the target's current state.</summary>
    WrongState = 6,

    /// <summary>The player does not own the target.</summary>
    NotOwner = 7,
}

/// <summary>The result the server sends back for an intent, and returns again for every repeat of the same id.</summary>
/// <param name="Status">Applied or rejected.</param>
/// <param name="Reason">Why it was rejected (<see cref="IntentRejectReason.None"/> when applied).</param>
/// <param name="Detail">Intent-specific detail, e.g. the bonding outcome of a Pact offer.</param>
public readonly record struct IntentResult(IntentStatus Status, IntentRejectReason Reason, byte Detail)
{
    /// <summary>An applied result.</summary>
    public static IntentResult Applied(byte detail = 0) => new(IntentStatus.Applied, IntentRejectReason.None, detail);

    /// <summary>A rejection.</summary>
    public static IntentResult Reject(IntentRejectReason reason) => new(IntentStatus.Rejected, reason, 0);

    /// <summary>True when the intent was applied.</summary>
    public bool IsApplied => Status == IntentStatus.Applied;
}

/// <summary>What the dedupe window knows about an intent id.</summary>
public enum IntentCheck : byte
{
    /// <summary>Never seen: process it.</summary>
    New,

    /// <summary>Seen before: return the original result, change nothing.</summary>
    Duplicate,

    /// <summary>Older than the window or lower than an id already seen: never apply it.</summary>
    Stale,
}

/// <summary>
/// The <see cref="IntentId"/> dedupe window (TDD §3.4): the last <see cref="IntentDeduplicator.Window"/> ids per session. A repeated
/// id returns the first result and changes nothing; from M1-17 the record is written in the same database transaction as the effect.
/// </summary>
public interface IIntentDeduplicator
{
    /// <summary>Sessions with a window.</summary>
    int Sessions { get; }

    /// <summary>Looks an id up; <paramref name="original"/> is set for duplicates.</summary>
    IntentCheck Check(IntentId id, out IntentResult original);

    /// <summary>Records the result of a <see cref="IntentCheck.New"/> id.</summary>
    void Record(IntentId id, IntentResult result, ulong now);

    /// <summary>Drops a session's window (the session ended for good).</summary>
    void Forget(ulong session);

    /// <summary>Drops windows of sessions idle for longer than <see cref="IntentDeduplicator.MaxIdleTicks"/>; returns how many.</summary>
    int Prune(ulong now);
}

/// <summary>
/// Ring buffer of the last 1,024 ids per session. Because sequences only increase, an id that is not in the ring but not higher than
/// the highest seen is <see cref="IntentCheck.Stale"/>, so nothing can be applied twice however late it is resent.
/// </summary>
public sealed class IntentDeduplicator : IIntentDeduplicator
{
    /// <summary>Ids remembered per session.</summary>
    public const int Window = 1024;

    /// <summary>10 minutes at 30 Hz: idle sessions are pruned after this (longer than the 2-minute reconnect window).</summary>
    public const ulong MaxIdleTicks = 18_000;

    private readonly Dictionary<ulong, SessionWindow> _sessions = [];
    private readonly List<ulong> _expired = [];

    /// <inheritdoc />
    public int Sessions => _sessions.Count;

    /// <inheritdoc />
    public IntentCheck Check(IntentId id, out IntentResult original)
    {
        original = default;
        if (!_sessions.TryGetValue(id.Session, out SessionWindow? window) || window.Count == 0)
        {
            return IntentCheck.New;
        }

        ref Entry entry = ref window.Entries[id.Sequence % Window];
        if (entry.Used && entry.Sequence == id.Sequence)
        {
            original = entry.Result;
            return IntentCheck.Duplicate;
        }

        return id.Sequence <= window.Highest ? IntentCheck.Stale : IntentCheck.New;
    }

    /// <inheritdoc />
    public void Record(IntentId id, IntentResult result, ulong now)
    {
        if (!_sessions.TryGetValue(id.Session, out SessionWindow? window))
        {
            window = new SessionWindow();
            _sessions.Add(id.Session, window);
        }

        if (window.Count > 0 && id.Sequence <= window.Highest)
        {
            throw new InvalidOperationException($"Intent {id} is not new; check it before recording.");
        }

        window.Entries[id.Sequence % Window] = new Entry { Used = true, Sequence = id.Sequence, Result = result };
        window.Highest = id.Sequence;
        window.Count++;
        window.LastActive = now;
    }

    /// <inheritdoc />
    public void Forget(ulong session) => _sessions.Remove(session);

    /// <inheritdoc />
    public int Prune(ulong now)
    {
        _expired.Clear();
        foreach ((ulong session, SessionWindow window) in _sessions)
        {
            if (now - window.LastActive > MaxIdleTicks)
            {
                _expired.Add(session);
            }
        }

        foreach (ulong session in _expired)
        {
            _sessions.Remove(session);
        }

        return _expired.Count;
    }

    private struct Entry
    {
        public bool Used;
        public uint Sequence;
        public IntentResult Result;
    }

    private sealed class SessionWindow
    {
        public Entry[] Entries { get; } = new Entry[Window];

        public uint Highest { get; set; }

        public long Count { get; set; }

        public ulong LastActive { get; set; }
    }
}
