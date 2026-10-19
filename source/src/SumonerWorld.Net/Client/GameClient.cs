using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Movement;
using SumonerWorld.Net.Messages;
using SumonerWorld.Net.Sessions;
using SumonerWorld.Net.Transport;

namespace SumonerWorld.Net.Client;

/// <summary>
/// The client side of the authoritative loop (TDD §3.2): opens the session, predicts the own character, sends the newest inputs
/// every tick (unreliable, redundant) and intents (reliable), and keeps the latest snapshot's entities. Remote entities are shown as
/// last received; the 100 ms interpolation buffer belongs to creature presentation (M1-21).
/// </summary>
public sealed class GameClient : ISessionMessageHandler
{
    private readonly ITransport _transport;
    private readonly ILog _log;
    private readonly PlayerInput[] _batch = new PlayerInput[InputBatch.MaxInputs];
    private readonly byte[] _buffer = new byte[Protocol.MaxMessageSize];
    private readonly Dictionary<(SnapshotEntityKind Kind, ulong Id), SnapshotEntity> _entities = [];
    private readonly Dictionary<uint, IntentResult> _results = [];
    private readonly IntentRequestCodec _intentCodec = new();
    private readonly IntentAckCodec _ackCodec = new();
    private uint _lastIntent;

    /// <summary>Creates the client; <paramref name="movement"/> must match the server's.</summary>
    public GameClient(ITransport transport, ILog log, Hello hello, MovementSettings movement)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(log);

        _transport = transport;
        _log = log;
        Session = new ClientEndpoint(transport, log, hello) { Messages = this };
        Prediction = new ClientPrediction(new MovementModel(movement));
    }

    /// <summary>The session handshake.</summary>
    public ClientEndpoint Session { get; }

    /// <summary>Own-character prediction.</summary>
    public ClientPrediction Prediction { get; }

    /// <summary>Tick of the newest snapshot received (0 = none yet).</summary>
    public ulong LastSnapshotTick { get; private set; }

    /// <summary>Entities in the newest snapshot, by kind and id.</summary>
    public IReadOnlyDictionary<(SnapshotEntityKind Kind, ulong Id), SnapshotEntity> Entities => _entities;

    /// <summary>Results of sent intents, by sequence (absent while waiting).</summary>
    public IReadOnlyDictionary<uint, IntentResult> IntentResults => _results;

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
                break;
            default:
                _log.Write(LogLevel.Warning, "client.unexpected", $"{type} is not a server message");
                break;
        }
    }

    private void ReadSnapshot(ref MessageReader reader)
    {
        SnapshotHeader header = Snapshot.ReadHeader(ref reader);
        if (header.ServerTick < LastSnapshotTick)
        {
            return;
        }

        if (header.ServerTick > LastSnapshotTick)
        {
            LastSnapshotTick = header.ServerTick;
            _entities.Clear();
            Prediction.Reconcile(header.AckedInput, header.OwnPosition);
        }

        for (int i = 0; i < header.EntityCount; i++)
        {
            SnapshotEntity entity = Snapshot.ReadEntity(ref reader);
            _entities[(entity.Kind, entity.Id)] = entity;
        }

        reader.EnsureEnd();
    }
}
