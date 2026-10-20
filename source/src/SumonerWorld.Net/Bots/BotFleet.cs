using System.Numerics;
using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Movement;
using SumonerWorld.Net.Client;
using SumonerWorld.Net.Messages;
using SumonerWorld.Net.Sessions;
using SumonerWorld.Net.Transport;

namespace SumonerWorld.Net.Bots;

/// <summary>Settings of a <see cref="BotFleet"/>.</summary>
public sealed class BotFleetSettings
{
    /// <summary>Bots (B2: 16).</summary>
    public int Count { get; init; } = 16;

    /// <summary>Account of the first bot; the others follow.</summary>
    public ulong FirstAccount { get; init; } = 3001;

    /// <summary>Dev authentication secret (must match the server).</summary>
    public string DevSecret { get; init; } = Hosting.HostOptions.DefaultDevSecret;

    /// <summary>Seed of the recorded inputs; bot <c>i</c> uses <c>Seed + i</c>.</summary>
    public ulong Seed { get; init; } = 0xB0752;

    /// <summary>Recorded ticks per bot (10 min 30 s at 30 Hz covers a full B2 run without repeating).</summary>
    public int ScriptTicks { get; init; } = 18_900;

    /// <summary>How far a bot walks from where it started, in metres.</summary>
    public float Leash { get; init; } = 40f;

    /// <summary>Each bot offers a Pact to the nearest creature it sees this often (0 = never).</summary>
    public int IntentEveryTicks { get; init; } = 300;

    /// <summary>Tick at which the first bot drops its connection without a goodbye and resumes the session (negative = off).</summary>
    public long ReconnectCheckAt { get; init; } = 600;

    /// <summary>Tick at which the second bot sends an intent and, once answered, sends it again (negative = off).</summary>
    public long DuplicateCheckAt { get; init; } = 450;

    /// <summary>Ticks a check may take before it fails.</summary>
    public long CheckTimeoutTicks { get; init; } = 600;

    /// <summary>Movement settings; must match the server's.</summary>
    public MovementSettings Movement { get; init; } = new();
}

/// <summary>State of one of the fleet's spike 2 checks.</summary>
public enum BotCheck
{
    /// <summary>Switched off in the settings.</summary>
    Off,

    /// <summary>Its tick has not come yet.</summary>
    Waiting,

    /// <summary>Started, not decided.</summary>
    Running,

    /// <summary>Passed.</summary>
    Passed,

    /// <summary>Failed (see the log).</summary>
    Failed,
}

/// <summary>What a fleet saw.</summary>
/// <param name="Bots">Bots in the fleet.</param>
/// <param name="InSession">Bots in a session now.</param>
/// <param name="SnapshotParts">Snapshot parts received by all bots.</param>
/// <param name="IntentsSent">Intents sent by all bots.</param>
/// <param name="IntentsAnswered">Distinct intents answered.</param>
/// <param name="LostIntents">Intents whose session ended before an answer.</param>
/// <param name="Reconnect">Drop and resume check.</param>
/// <param name="Duplicate">Repeated intent check.</param>
public sealed record BotFleetReport(
    int Bots, int InSession, long SnapshotParts, int IntentsSent, int IntentsAnswered, int LostIntents, BotCheck Reconnect, BotCheck Duplicate)
{
    /// <summary>Every bot is in a session and every switched-on check passed.</summary>
    public bool Passed => InSession == Bots && Reconnect is BotCheck.Off or BotCheck.Passed && Duplicate is BotCheck.Off or BotCheck.Passed;
}

/// <summary>
/// Bot clients for the B2 benchmark and spike 2 (TDD §8): each bot is a real <see cref="GameClient"/> with prediction that replays
/// its <see cref="BotScript"/> and now and then offers a Pact to the nearest creature it sees. Two bots also run the spike 2 checks:
/// <list type="bullet">
/// <item><b>Reconnect:</b> the first bot drops its connection without a goodbye (as when a network changes) and must get its session
/// back (<see cref="Welcome.Resumed"/>, same token) and snapshots again.</item>
/// <item><b>Duplicate:</b> the second bot sends an intent and, once answered, sends the same intent again; the second answer must equal
/// the first. That it was not run twice shows on the server: <see cref="Core.Transactions.TransactionProcessor.Duplicates"/> counts
/// it and nothing else changes.</item>
/// </list>
/// The fleet does not flush transports; the host does that after <see cref="Step"/>.
/// </summary>
public sealed class BotFleet
{
    private readonly BotFleetSettings _settings;
    private readonly Func<ITransport> _connect;
    private readonly ILog _log;
    private readonly List<Bot> _bots = [];
    private ulong _reconnectToken;
    private long _reconnectStarted;
    private IntentRequest _duplicate;
    private long _duplicateStarted;
    private IntentResult _firstResult;
    private int _acksBeforeRepeat = -1;

    /// <summary>Creates the bots; <paramref name="connect"/> returns a new transport that is connecting to the server.</summary>
    public BotFleet(BotFleetSettings settings, Func<ITransport> connect, ILog log)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(connect);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(settings.Count);

        _settings = settings;
        _connect = connect;
        _log = log;
        for (int i = 0; i < settings.Count; i++)
        {
            ulong account = settings.FirstAccount + (ulong)i;
            ITransport transport = connect();
            var hello = new Hello(0, DevTokenAuthenticator.Ticket(account, settings.DevSecret));
            var client = new GameClient(transport, log, hello, settings.Movement);
            var script = BotScript.Record(settings.Seed + (ulong)i, settings.ScriptTicks, settings.Movement, settings.Leash);
            _bots.Add(new Bot(account, client, transport, script, (i * 19) % Math.Max(1, settings.IntentEveryTicks)));
        }

        Reconnect = settings.ReconnectCheckAt >= 0 ? BotCheck.Waiting : BotCheck.Off;
        Duplicate = settings.DuplicateCheckAt >= 0 && settings.Count > 1 ? BotCheck.Waiting : BotCheck.Off;
    }

    /// <summary>Ticks run.</summary>
    public long Tick { get; private set; }

    /// <summary>The drop-and-resume check.</summary>
    public BotCheck Reconnect { get; private set; }

    /// <summary>The repeated-intent check.</summary>
    public BotCheck Duplicate { get; private set; }

    /// <summary>The bots.</summary>
    public IReadOnlyList<Bot> Bots => _bots;

    /// <summary>One tick of every bot.</summary>
    public void Step()
    {
        foreach (Bot bot in _bots)
        {
            bot.Client.Tick(bot.Script.InputAt(Tick));
            if (_settings.IntentEveryTicks > 0 && Tick % _settings.IntentEveryTicks == bot.IntentPhase)
            {
                bot.OfferToNearest();
            }
        }

        RunReconnectCheck();
        RunDuplicateCheck();
        Tick++;
    }

    /// <summary>What the fleet saw so far.</summary>
    public BotFleetReport Report()
    {
        int inSession = 0;
        long parts = 0;
        int sent = 0;
        int answered = 0;
        int lost = 0;
        foreach (Bot bot in _bots)
        {
            inSession += bot.Client.Session.State == ClientSessionState.InSession ? 1 : 0;
            parts += bot.Client.SnapshotPartsReceived;
            sent += bot.IntentsSent;
            answered += bot.Client.IntentResults.Count;
            lost += bot.Client.LostIntents;
        }

        return new BotFleetReport(_bots.Count, inSession, parts, sent, answered, lost, Reconnect, Duplicate);
    }

    private void RunReconnectCheck()
    {
        Bot bot = _bots[0];
        if (Reconnect == BotCheck.Waiting && Tick >= _settings.ReconnectCheckAt && bot.Client.LastSnapshotTick > 0)
        {
            _reconnectToken = bot.Client.Session.Session!.Value.SessionToken;
            _reconnectStarted = Tick;
            (bot.Transport as IDisposable)?.Dispose();
            bot.Transport = _connect();
            bot.Client.Reconnect(bot.Transport);
            Reconnect = BotCheck.Running;
            _log.Write(LogLevel.Info, "bot.reconnect", $"account {bot.Account} dropped its connection at tick {Tick}");
        }
        else if (Reconnect == BotCheck.Running)
        {
            Welcome? session = bot.Client.Session.Session;
            if (bot.Client.Session.State == ClientSessionState.InSession && bot.Client.LastSnapshotTick > 0 && session is Welcome welcome)
            {
                bool resumed = welcome.Resumed && welcome.SessionToken == _reconnectToken;
                Finish(isReconnect: true, resumed, $"account {bot.Account}: {(resumed ? "session resumed" : "got a new session")} after {Tick - _reconnectStarted} ticks");
            }
            else if (Tick - _reconnectStarted > _settings.CheckTimeoutTicks)
            {
                Finish(isReconnect: true, false, $"account {bot.Account}: no session after {_settings.CheckTimeoutTicks} ticks ({bot.Client.Session.State})");
            }
        }
    }

    private void RunDuplicateCheck()
    {
        if (Duplicate == BotCheck.Off)
        {
            return;
        }

        Bot bot = _bots[1];
        if (Duplicate == BotCheck.Waiting && Tick >= _settings.DuplicateCheckAt && bot.Client.LastSnapshotTick > 0 && bot.Nearest() is ulong target)
        {
            uint sequence = bot.Client.SendIntent(IntentKind.OfferPact, target, Bot.PactTerms);
            bot.IntentsSent++;
            _duplicate = new IntentRequest(sequence, IntentKind.OfferPact, target, Bot.PactTerms);
            _duplicateStarted = Tick;
            Duplicate = BotCheck.Running;
        }
        else if (Duplicate == BotCheck.Running)
        {
            if (_acksBeforeRepeat < 0 && bot.Client.IntentResults.TryGetValue(_duplicate.Sequence, out IntentResult first))
            {
                _firstResult = first;
                _acksBeforeRepeat = bot.Client.AcksReceived;
                bot.Client.Resend(_duplicate);
            }
            else if (_acksBeforeRepeat >= 0 && bot.Client.AcksReceived > _acksBeforeRepeat)
            {
                IntentResult second = bot.Client.IntentResults[_duplicate.Sequence];
                Finish(isReconnect: false, second == _firstResult, $"account {bot.Account} intent {_duplicate.Sequence}: first {Describe(_firstResult)}, repeat {Describe(second)}");
            }
            else if (Tick - _duplicateStarted > _settings.CheckTimeoutTicks)
            {
                Finish(isReconnect: false, false, $"account {bot.Account} intent {_duplicate.Sequence}: no answer after {_settings.CheckTimeoutTicks} ticks");
            }
        }
    }

    private void Finish(bool isReconnect, bool passed, string detail)
    {
        BotCheck result = passed ? BotCheck.Passed : BotCheck.Failed;
        if (isReconnect)
        {
            Reconnect = result;
        }
        else
        {
            Duplicate = result;
        }

        string name = isReconnect ? "reconnect" : "duplicate";
        _log.Write(passed ? LogLevel.Info : LogLevel.Error, $"bot.check_{(passed ? "passed" : "failed")}", $"{name}: {detail}");
    }

    private static string Describe(IntentResult result) => result.IsApplied ? $"applied ({result.Detail})" : $"rejected ({result.Reason})";
}

/// <summary>One bot of a <see cref="BotFleet"/>.</summary>
public sealed class Bot
{
    /// <summary>Pact terms the bots offer (the first catalogue entry).</summary>
    public const byte PactTerms = 0b1;

    internal Bot(ulong account, GameClient client, ITransport transport, BotScript script, int intentPhase)
    {
        Account = account;
        Client = client;
        Transport = transport;
        Script = script;
        IntentPhase = intentPhase;
    }

    /// <summary>Its account.</summary>
    public ulong Account { get; }

    /// <summary>Its client.</summary>
    public GameClient Client { get; }

    /// <summary>The transport in use (replaced by the reconnect check).</summary>
    public ITransport Transport { get; internal set; }

    /// <summary>Its recorded inputs.</summary>
    public BotScript Script { get; }

    /// <summary>Intents it sent.</summary>
    public int IntentsSent { get; internal set; }

    internal int IntentPhase { get; }

    internal void OfferToNearest()
    {
        if (Client.Session.State == ClientSessionState.InSession && Client.LastSnapshotTick > 0 && Nearest() is ulong target)
        {
            Client.SendIntent(IntentKind.OfferPact, target, PactTerms);
            IntentsSent++;
        }
    }

    internal ulong? Nearest()
    {
        Vector3 own = Client.Prediction.Position;
        ulong? nearest = null;
        float best = float.MaxValue;
        foreach (SnapshotEntity entity in Client.Entities.Values)
        {
            float distance = Vector3.DistanceSquared(own, entity.Position);
            if (entity.Kind == SnapshotEntityKind.Creature && distance < best)
            {
                (nearest, best) = (entity.Id, distance);
            }
        }

        return nearest;
    }
}
