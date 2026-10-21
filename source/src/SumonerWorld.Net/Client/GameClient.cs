using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Movement;
using SumonerWorld.Core.World;
using SumonerWorld.Net.Messages;
using SumonerWorld.Net.Sessions;
using SumonerWorld.Net.Transport;

namespace SumonerWorld.Net.Client;

/// <summary>
/// The client side of the authoritative loop (TDD §3.2): opens the session, predicts the own character, sends the newest inputs
/// every tick (unreliable, redundant) and intents (reliable), and keeps the latest snapshot's entities. After a lost connection,
/// <see cref="Reconnect"/> resumes the session (TDD §3.5): prediction restarts from the next snapshot and unanswered intents are sent
/// again with their sequences (the server's dedupe window answers repeats with the first result). Remote entities also go into
/// <see cref="Remote"/>, the 100 ms interpolation buffer the presentation draws from (M1-21). Dev tools (M1-22):
/// <see cref="RequestDevTools"/> asks a dev server for <see cref="DevMetrics"/> and an <see cref="Inspection"/>; the request is sent
/// again after every welcome.
/// </summary>
public sealed class GameClient : IClientSessionHandler
{
    private readonly SortedDictionary<uint, IntentRequest> _unanswered = [];
    private ITransport _transport;
    private readonly ILog _log;
    private readonly PlayerInput[] _batch = new PlayerInput[InputBatch.MaxInputs];
    private readonly byte[] _buffer = new byte[Protocol.MaxMessageSize];
    private readonly Dictionary<(SnapshotEntityKind Kind, ulong Id), SnapshotEntity> _entities = [];
    private readonly Dictionary<uint, IntentResult> _results = [];
    private readonly IntentRequestCodec _intentCodec = new();
    private readonly IntentAckCodec _ackCodec = new();
    private readonly DevRequestCodec _devRequestCodec = new();
    private readonly ServerMetricsCodec _metricsCodec = new();
    private readonly DevInspectionCodec _inspectionCodec = new();
    private readonly CompanionListCodec _companionCodec = new();
    private uint _lastIntent;

    /// <summary>Creates the client; <paramref name="movement"/> must match the server's.</summary>
    public GameClient(ITransport transport, ILog log, Hello hello, MovementSettings movement, DodgePrediction? dodge = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(log);

        _transport = transport;
        _log = log;
        Session = new ClientEndpoint(transport, log, hello) { Messages = this };
        Prediction = new ClientPrediction(new MovementModel(movement)) { Dodge = dodge };
    }

    /// <summary>The session handshake.</summary>
    public ClientEndpoint Session { get; }

    /// <summary>Own-character prediction.</summary>
    public ClientPrediction Prediction { get; }

    /// <summary>Tick of the newest snapshot received (0 = none yet).</summary>
    public ulong LastSnapshotTick { get; private set; }

    /// <summary>Remote entities for presentation, interpolated 100 ms in the past; the host advances it every frame.</summary>
    public RemoteInterpolation Remote { get; } = new();

    /// <summary>True when the server allows lock-on (Normal preset; not on Hardcore).</summary>
    public bool LockOnAllowed => Session.Session is Welcome welcome && (welcome.Rules & ServerRules.LockOn) != 0;

    /// <summary>The player's pacted creatures, as the server last sent them (since protocol 9).</summary>
    public IReadOnlyList<CompanionEntry> Companions { get; private set; } = [];

    /// <summary>The own character's vitals and combat action from the newest snapshot (since protocol 7).</summary>
    public SnapshotVitals OwnVitals { get; private set; } = new(byte.MaxValue, byte.MaxValue, byte.MaxValue, 0);

    /// <summary>Entities in the newest snapshot, by kind and id.</summary>
    public IReadOnlyDictionary<(SnapshotEntityKind Kind, ulong Id), SnapshotEntity> Entities => _entities;

    /// <summary>Results of sent intents, by sequence (absent while waiting).</summary>
    public IReadOnlyDictionary<uint, IntentResult> IntentResults => _results;

    /// <summary>Intents sent but not answered yet.</summary>
    public int UnansweredIntents => _unanswered.Count;

    /// <summary>Intent results received, repeats included.</summary>
    public int AcksReceived { get; private set; }

    /// <summary>Snapshot parts received.</summary>
    public int SnapshotPartsReceived { get; private set; }

    /// <summary>Intents dropped because the server started a new session instead of resuming (their outcome is unknown).</summary>
    public int LostIntents { get; private set; }

    /// <summary>What the dev tools asked the server for.</summary>
    public DevRequest DevRequest { get; private set; }

    /// <summary>The newest server metrics (dev overlay); null until a dev server sent some.</summary>
    public ServerMetrics? DevMetrics { get; private set; }

    /// <summary>The newest inspection of <see cref="DevRequest"/>'s creature; null until one arrived or when it is gone.</summary>
    public CreatureInspection? Inspection { get; private set; }

    /// <summary>The server answered that the inspected creature does not exist (any more).</summary>
    public bool InspectionGone { get; private set; }

    /// <summary>
    /// Asks the server for dev data: <paramref name="metrics"/> for the overlay and the creature with stable id
    /// <paramref name="inspect"/> (0 = none). Sent now when in a session and again after every welcome. A server without dev tools
    /// ignores it.
    /// </summary>
    public void RequestDevTools(bool metrics, ulong inspect)
    {
        if (inspect != DevRequest.Inspect)
        {
            Inspection = null;
            InspectionGone = false;
        }

        DevMetrics = metrics ? DevMetrics : null;
        DevRequest = new DevRequest(metrics, inspect);
        if (Session.State == ClientSessionState.InSession)
        {
            SendDevRequest();
        }
    }

    /// <summary>Uses <paramref name="transport"/> for the next connection; connect it to the server afterwards.</summary>
    public void Reconnect(ITransport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        _transport = transport;
        Session.Rebind(transport);
    }

    /// <inheritdoc />
    public void OnWelcome(in Welcome welcome)
    {
        Prediction.Reset();
        _entities.Clear();
        Remote.Clear();
        LastSnapshotTick = 0;
        if (DevRequest != default)
        {
            SendDevRequest();
        }

        if (!welcome.Resumed)
        {
            LostIntents += _unanswered.Count;
            _unanswered.Clear();
            return;
        }

        foreach (IntentRequest request in _unanswered.Values)
        {
            Resend(request);
        }
    }

    /// <summary>
    /// One client tick: receive, then (once the first snapshot arrived) predict <paramref name="input"/> and send the newest inputs.
    /// The host flushes the transport afterwards.
    /// </summary>
    public void Tick(MoveInput input, byte buttons = 0)
    {
        _transport.Poll(Session);
        if (Session.State != ClientSessionState.InSession || !Prediction.IsInitialized)
        {
            return;
        }

        Prediction.Record(input, buttons);
        int count = Prediction.CopyNewest(_batch);
        var writer = new MessageWriter(_buffer);
        InputBatch.Write(ref writer, _batch.AsSpan(0, count));
        _transport.Send(Session.Connection, Channel.Unreliable, writer.WrittenSpan);
    }

    /// <summary>Sends an intent; returns its sequence (the result appears in <see cref="IntentResults"/>).</summary>
    public uint SendIntent(IntentKind kind, ulong target, byte argument)
    {
        if (Session.State != ClientSessionState.InSession)
        {
            throw new InvalidOperationException("Not in a session.");
        }

        var request = new IntentRequest(++_lastIntent, kind, target, argument);
        _unanswered.Add(request.Sequence, request);
        Resend(request);
        return request.Sequence;
    }

    /// <summary>Sends an intent again with the same sequence (after a reconnect; the server answers with the first result).</summary>
    public void Resend(in IntentRequest request)
    {
        var writer = new MessageWriter(_buffer);
        _intentCodec.Write(ref writer, request);
        _transport.Send(Session.Connection, Channel.ReliableOrdered, writer.WrittenSpan);
    }

    /// <inheritdoc />
    public void OnMessage(ConnectionId connection, MessageType type, ref MessageReader reader)
    {
        switch (type)
        {
            case MessageType.Snapshot:
                ReadSnapshot(ref reader);
                break;
            case MessageType.IntentAck:
                IntentAck ack = _ackCodec.Read(ref reader);
                _results[ack.Sequence] = ack.Result;
                AcksReceived++;
                _unanswered.Remove(ack.Sequence);
                break;
            case MessageType.CompanionList:
                Companions = _companionCodec.Read(ref reader).Entries;
                break;
            case MessageType.DevMetrics:
                ServerMetrics metrics = _metricsCodec.Read(ref reader);
                DevMetrics = DevRequest.Metrics ? metrics : null;
                break;
            case MessageType.DevInspection:
                DevInspection inspection = _inspectionCodec.Read(ref reader);
                if (inspection.Requested == DevRequest.Inspect)
                {
                    Inspection = inspection.Inspection;
                    InspectionGone = inspection.Inspection is null;
                }

                break;
            default:
                _log.Write(LogLevel.Warning, "client.unexpected", $"{type} is not a server message");
                break;
        }
    }

    private void SendDevRequest()
    {
        var writer = new MessageWriter(_buffer);
        _devRequestCodec.Write(ref writer, DevRequest);
        _transport.Send(Session.Connection, Channel.ReliableOrdered, writer.WrittenSpan);
    }

    private void ReadSnapshot(ref MessageReader reader)
    {
        SnapshotHeader header = Snapshot.ReadHeader(ref reader);
        SnapshotPartsReceived++;
        if (header.ServerTick < LastSnapshotTick)
        {
            return;
        }

        if (header.ServerTick > LastSnapshotTick)
        {
            LastSnapshotTick = header.ServerTick;
            _entities.Clear();
            Prediction.Reconcile(header.AckedInput, header.OwnPosition);
            OwnVitals = header.Own;
            Prediction.Frozen = header.Own.Action is (byte)CombatAction.Down or (byte)CombatAction.Stagger or (byte)CombatAction.Cast;
        }

        for (int i = 0; i < header.EntityCount; i++)
        {
            SnapshotEntity entity = Snapshot.ReadEntity(ref reader);
            _entities[(entity.Kind, entity.Id)] = entity;
            Remote.Add(header.ServerTick, entity);
        }

        reader.EnsureEnd();
    }
}
