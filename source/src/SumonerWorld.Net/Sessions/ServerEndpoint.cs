using System.Globalization;
using System.Security.Cryptography;
using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Net.Messages;
using SumonerWorld.Net.Transport;

namespace SumonerWorld.Net.Sessions;

/// <summary>
/// The server side of sessions (TDD §3.3, §3.5). A client must send a valid <see cref="Hello"/> first; the server answers
/// <see cref="Welcome"/> or <see cref="Rejected"/> and disconnects. A disconnect <b>suspends</b> the session: a Hello with its secret
/// token from the same account within <see cref="ReconnectWindowTicks"/> resumes it (same session id, so the intent dedupe window
/// still applies); after that it closes. One session per account: a new login replaces the old one. Malformed messages are logged
/// and the sender's session is closed for good (R31). Messages after the handshake go to the <see cref="IServerSessionHandler"/>.
/// </summary>
public sealed class ServerEndpoint : ITransportHandler
{
    /// <summary>2 minutes at 30 Hz: how long a suspended session can be resumed.</summary>
    public const ulong ReconnectWindowTicks = 3_600;

    private readonly ITransport _transport;
    private readonly IAuthenticator _authenticator;
    private readonly ILog _log;
    private readonly int _maxPlayers;
    private readonly IServerSessionHandler? _sessions;
    private readonly Dictionary<ulong, SessionRecord> _byToken = [];
    private readonly Dictionary<ConnectionId, SessionRecord> _byConnection = [];
    private readonly HashSet<ConnectionId> _pending = [];
    private readonly List<SessionRecord> _expired = [];
    private readonly byte[] _buffer = new byte[Protocol.MaxMessageSize];
    private readonly HelloCodec _hello = new();
    private readonly WelcomeCodec _welcome = new();
    private readonly RejectedCodec _rejected = new();
    private ulong _lastSessionId;

    /// <summary>The server's data fingerprint (M2-15); a client with other data is refused. 0 = not checked.</summary>
    public ulong DataFingerprint { get; init; }

    /// <summary>Creates the endpoint.</summary>
    public ServerEndpoint(
        ITransport transport, IAuthenticator authenticator, ILog log, int maxPlayers, IServerSessionHandler? sessions = null, ulong lastSessionId = 0)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(authenticator);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPlayers);

        _transport = transport;
        _authenticator = authenticator;
        _log = log;
        _maxPlayers = maxPlayers;
        _sessions = sessions;
        _lastSessionId = lastSessionId;
    }

    /// <summary>The current server tick, sent in <see cref="Welcome"/> (set by <see cref="Update"/>).</summary>
    public ulong ServerTick { get; private set; }

    /// <summary>The rules sent to every client in its <see cref="Welcome"/> (set by the game server).</summary>
    public ServerRules Rules { get; set; }

    /// <summary>Players with a connected session.</summary>
    public int PlayerCount => _byConnection.Count;

    /// <summary>Sessions waiting for a reconnect.</summary>
    public int SuspendedCount => _byToken.Count - _byConnection.Count;

    /// <summary>The account of a connection with an open session, if any.</summary>
    public bool TryGetAccount(ConnectionId connection, out ulong accountId)
    {
        bool found = _byConnection.TryGetValue(connection, out SessionRecord? session);
        accountId = found ? session!.Account : 0;
        return found;
    }

    /// <summary>Advances the session clock and closes suspended sessions whose reconnect window has passed.</summary>
    public void Update(ulong serverTick)
    {
        ServerTick = serverTick;
        _expired.Clear();
        foreach (SessionRecord session in _byToken.Values)
        {
            if (session.Connection is null && serverTick - session.SuspendedAt > ReconnectWindowTicks)
            {
                _expired.Add(session);
            }
        }

        foreach (SessionRecord session in _expired)
        {
            Log(LogLevel.Info, "session.expired", $"account {session.Account} did not reconnect in time");
            Close(session);
        }
    }

    /// <inheritdoc />
    public void OnConnected(ConnectionId connection)
    {
        _pending.Add(connection);
        Log(LogLevel.Info, "session.connected", $"connection {connection.Value} opened; waiting for Hello");
    }

    /// <inheritdoc />
    public void OnDisconnected(ConnectionId connection, DisconnectReason reason)
    {
        _pending.Remove(connection);
        if (_byConnection.Remove(connection, out SessionRecord? session))
        {
            session.Connection = null;
            session.SuspendedAt = ServerTick;
            Log(LogLevel.Info, "session.suspended", $"account {session.Account} disconnected ({reason}); resumable for {ReconnectWindowTicks} ticks");
            _sessions?.OnSessionSuspended(session.Id);
        }
    }

    /// <inheritdoc />
    public void OnData(ConnectionId connection, Channel channel, ReadOnlySpan<byte> payload)
    {
        try
        {
            var reader = new MessageReader(payload);
            MessageType type = Protocol.ReadHeader(ref reader);
            if (_pending.Contains(connection))
            {
                if (type != MessageType.Hello)
                {
                    throw new MalformedMessageException($"Expected Hello first, got {type}.");
                }

                HandleHello(connection, _hello.Read(ref reader));
            }
            else if (_byConnection.ContainsKey(connection))
            {
                if (type == MessageType.Hello)
                {
                    throw new MalformedMessageException("Hello sent twice.");
                }

                _sessions?.OnMessage(connection, type, ref reader);
            }
        }
        catch (MalformedMessageException exception)
        {
            Log(LogLevel.Warning, "session.malformed", $"connection {connection.Value}: {exception.Message}");
            Drop(connection);
        }
    }

    private void HandleHello(ConnectionId connection, Hello hello)
    {
        _pending.Remove(connection);
        if (!_authenticator.TryAuthenticate(hello.AuthTicket, out ulong account, out RejectReason reason))
        {
            Reject(connection, reason, "Authentication failed.");
            return;
        }

        if (hello.DataFingerprint != 0 && DataFingerprint != 0 && hello.DataFingerprint != DataFingerprint)
        {
            Reject(connection, RejectReason.DataMismatch, "Your game data differs from the server's. Get the new package.");
            return;
        }

        if (hello.ResumeToken != 0 && _byToken.TryGetValue(hello.ResumeToken, out SessionRecord? resumed) && resumed.Account == account)
        {
            Resume(connection, resumed);
            return;
        }

        if (FindByAccount(account) is SessionRecord previous)
        {
            Log(LogLevel.Info, "session.replaced", $"account {account} logged in again; the old session ends");
            Close(previous);
        }

        if (_byToken.Count >= _maxPlayers)
        {
            Reject(connection, RejectReason.ServerFull, string.Create(CultureInfo.InvariantCulture, $"{_maxPlayers}/{_maxPlayers} players."));
            return;
        }

        var session = new SessionRecord(NewToken(), ++_lastSessionId, account) { Connection = connection };
        _byToken.Add(session.Token, session);
        _byConnection.Add(connection, session);
        SendWelcome(connection, session, resumed: false);
        Log(LogLevel.Info, "session.welcome", $"account {account} joined on connection {connection.Value}");
        _sessions?.OnSessionOpened(connection, account, session.Id);
    }

    private void Resume(ConnectionId connection, SessionRecord session)
    {
        if (session.Connection is ConnectionId old)
        {
            // The client reconnected before the server noticed the old connection was gone.
            _byConnection.Remove(old);
            _transport.Disconnect(old);
        }
        else
        {
            session.SuspendedAt = 0;
        }

        session.Connection = connection;
        _byConnection.Add(connection, session);
        SendWelcome(connection, session, resumed: true);
        Log(LogLevel.Info, "session.resumed", $"account {session.Account} resumed on connection {connection.Value}");
        _sessions?.OnSessionResumed(connection, session.Id);
    }

    private SessionRecord? FindByAccount(ulong account)
    {
        foreach (SessionRecord session in _byToken.Values)
        {
            if (session.Account == account)
            {
                return session;
            }
        }

        return null;
    }

    private void SendWelcome(ConnectionId connection, SessionRecord session, bool resumed)
    {
        var writer = new MessageWriter(_buffer);
        _welcome.Write(ref writer, new Welcome(session.Token, session.Account, ServerTick, resumed, Rules));
        _transport.Send(connection, Channel.ReliableOrdered, writer.WrittenSpan);
    }

    private void Reject(ConnectionId connection, RejectReason reason, string detail)
    {
        var writer = new MessageWriter(_buffer);
        _rejected.Write(ref writer, new Rejected(reason, detail));
        _transport.Send(connection, Channel.ReliableOrdered, writer.WrittenSpan);
        Log(LogLevel.Warning, "session.rejected", $"connection {connection.Value}: {reason}");
        _transport.Disconnect(connection);
    }

    private void Drop(ConnectionId connection)
    {
        _pending.Remove(connection);
        if (_byConnection.TryGetValue(connection, out SessionRecord? session))
        {
            Close(session);
        }
        else
        {
            _transport.Disconnect(connection);
        }
    }

    private void Close(SessionRecord session)
    {
        _byToken.Remove(session.Token);
        if (session.Connection is ConnectionId connection)
        {
            _byConnection.Remove(connection);
            _transport.Disconnect(connection);
        }

        Log(LogLevel.Info, "session.closed", $"account {session.Account} session {session.Id} closed");
        _sessions?.OnSessionClosed(session.Id);
    }

    private ulong NewToken()
    {
        ulong token;
        do
        {
            token = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong)));
        }
        while (token == 0 || _byToken.ContainsKey(token));

        return token;
    }

    private void Log(LogLevel level, string eventName, string message)
    {
        if (_log.IsEnabled(level))
        {
            _log.Write(level, eventName, message);
        }
    }

    private sealed class SessionRecord(ulong token, ulong id, ulong account)
    {
        public ulong Token { get; } = token;

        public ulong Id { get; } = id;

        public ulong Account { get; } = account;

        public ConnectionId? Connection { get; set; }

        public ulong SuspendedAt { get; set; }
    }
}
