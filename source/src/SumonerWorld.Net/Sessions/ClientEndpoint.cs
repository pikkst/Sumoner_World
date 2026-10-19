using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Net.Messages;
using SumonerWorld.Net.Transport;

namespace SumonerWorld.Net.Sessions;

/// <summary>Where the client's session stands.</summary>
public enum ClientSessionState
{
    /// <summary>Not connected yet.</summary>
    Connecting,

    /// <summary>Connected; Hello sent, waiting for the answer.</summary>
    Authenticating,

    /// <summary>Welcome received; the session is open.</summary>
    InSession,

    /// <summary>The server refused (see <see cref="ClientEndpoint.RejectReason"/>).</summary>
    Rejected,

    /// <summary>The connection closed.</summary>
    Disconnected,
}

/// <summary>
/// The client side of the session handshake: sends <see cref="Hello"/> on connect and records the server's answer. Messages in the
/// session go to <see cref="Messages"/>.
/// </summary>
public sealed class ClientEndpoint : ITransportHandler
{
    private readonly ITransport _transport;
    private readonly ILog _log;
    private readonly Hello _hello;
    private readonly byte[] _buffer = new byte[Protocol.MaxMessageSize];

    /// <summary>Creates the endpoint; <paramref name="hello"/> is sent as soon as the connection opens.</summary>
    public ClientEndpoint(ITransport transport, ILog log, Hello hello)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(log);

        _transport = transport;
        _log = log;
        _hello = hello;
    }

    /// <summary>Receives messages after the handshake (snapshots, intent acks).</summary>
    public ISessionMessageHandler? Messages { get; set; }

    /// <summary>The connection to the server, once connected.</summary>
    public ConnectionId Connection { get; private set; }

    /// <summary>Current state.</summary>
    public ClientSessionState State { get; private set; } = ClientSessionState.Connecting;

    /// <summary>The session, once welcomed.</summary>
    public Welcome? Session { get; private set; }

    /// <summary>Why the server refused, if it did.</summary>
    public RejectReason? RejectReason { get; private set; }

    /// <inheritdoc />
    public void OnConnected(ConnectionId connection)
    {
        Connection = connection;
        var writer = new MessageWriter(_buffer);
        new HelloCodec().Write(ref writer, _hello);
        _transport.Send(connection, Channel.ReliableOrdered, writer.WrittenSpan);
        State = ClientSessionState.Authenticating;
    }

    /// <inheritdoc />
    public void OnDisconnected(ConnectionId connection, DisconnectReason reason)
    {
        if (State != ClientSessionState.Rejected)
        {
            State = ClientSessionState.Disconnected;
        }

        _log.Write(LogLevel.Info, "client.disconnected", $"connection closed ({reason})");
    }

    /// <inheritdoc />
    public void OnData(ConnectionId connection, Channel channel, ReadOnlySpan<byte> payload)
    {
        try
        {
            var reader = new MessageReader(payload);
            MessageType type = Protocol.ReadHeader(ref reader);
            switch (type)
            {
                case MessageType.Welcome:
                    Session = new WelcomeCodec().Read(ref reader);
                    State = ClientSessionState.InSession;
                    _log.Write(LogLevel.Info, "client.welcome", $"in session as account {Session.Value.AccountId}");
                    break;
                case MessageType.Rejected:
                    Rejected rejected = new RejectedCodec().Read(ref reader);
                    RejectReason = rejected.Reason;
                    State = ClientSessionState.Rejected;
                    _log.Write(LogLevel.Warning, "client.rejected", $"{rejected.Reason}: {rejected.Detail}");
                    break;
                default:
                    if (State == ClientSessionState.InSession)
                    {
                        Messages?.OnMessage(connection, type, ref reader);
                    }

                    break;
            }
        }
        catch (MalformedMessageException exception)
        {
            _log.Write(LogLevel.Warning, "client.malformed", exception.Message);
        }
    }
}
