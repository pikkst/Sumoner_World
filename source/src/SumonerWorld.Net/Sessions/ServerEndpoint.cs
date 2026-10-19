using System.Globalization;
using System.Security.Cryptography;
using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Net.Messages;
using SumonerWorld.Net.Transport;

namespace SumonerWorld.Net.Sessions;

/// <summary>
/// The server side of the session handshake (TDD §3.3): a client must send a valid <see cref="Hello"/> first; the server answers
/// <see cref="Welcome"/> with a fresh session token or <see cref="Rejected"/> and disconnects. Malformed messages are logged and the
/// sender is dropped (R31). Reconnect windows and logout linger arrive with M1-16.
/// </summary>
public sealed class ServerEndpoint : ITransportHandler
{
    private readonly ITransport _transport;
    private readonly IAuthenticator _authenticator;
    private readonly ILog _log;
    private readonly int _maxPlayers;
    private readonly Dictionary<ConnectionId, ulong> _accounts = [];
    private readonly HashSet<ConnectionId> _pending = [];
    private readonly byte[] _buffer = new byte[Protocol.MaxMessageSize];
    private readonly HelloCodec _hello = new();
    private readonly WelcomeCodec _welcome = new();
    private readonly RejectedCodec _rejected = new();

    /// <summary>Creates the endpoint.</summary>
    public ServerEndpoint(ITransport transport, IAuthenticator authenticator, ILog log, int maxPlayers)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(authenticator);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPlayers);

        _transport = transport;
        _authenticator = authenticator;
        _log = log;
        _maxPlayers = maxPlayers;
    }

    /// <summary>The current server tick, sent in <see cref="Welcome"/>.</summary>
    public ulong ServerTick { get; set; }

    /// <summary>Players with an open session.</summary>
    public int PlayerCount => _accounts.Count;

    /// <summary>The account of a connection with an open session, if any.</summary>
    public bool TryGetAccount(ConnectionId connection, out ulong accountId) => _accounts.TryGetValue(connection, out accountId);

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
        if (_accounts.Remove(connection, out ulong account))
        {
            Log(LogLevel.Info, "session.closed", $"account {account} left ({reason})");
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

        if (_accounts.Count >= _maxPlayers)
        {
            Reject(connection, RejectReason.ServerFull, string.Create(CultureInfo.InvariantCulture, $"{_maxPlayers}/{_maxPlayers} players."));
            return;
        }

        _accounts[connection] = account;
        ulong token = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong)));
        var writer = new MessageWriter(_buffer);
        _welcome.Write(ref writer, new Welcome(token, account, ServerTick));
        _transport.Send(connection, Channel.ReliableOrdered, writer.WrittenSpan);
        Log(LogLevel.Info, "session.welcome", $"account {account} joined on connection {connection.Value}");
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
        _accounts.Remove(connection);
        _transport.Disconnect(connection);
    }

    private void Log(LogLevel level, string eventName, string message)
    {
        if (_log.IsEnabled(level))
        {
            _log.Write(level, eventName, message);
        }
    }
}
