using SumonerWorld.Net.Messages;
using SumonerWorld.Net.Transport;

namespace SumonerWorld.Net.Sessions;

/// <summary>Receives the messages that follow the handshake, after the endpoint has checked the header.</summary>
public interface ISessionMessageHandler
{
    /// <summary>A message of <paramref name="type"/> arrived; <paramref name="reader"/> is positioned after the header.</summary>
    void OnMessage(ConnectionId connection, MessageType type, ref MessageReader reader);
}

/// <summary>The game side of the server's sessions: told when sessions open and close, and given their messages.</summary>
public interface IServerSessionHandler : ISessionMessageHandler
{
    /// <summary>A session opened. <paramref name="sessionId"/> is server-assigned, unique for the server's life and not secret.</summary>
    void OnSessionOpened(ConnectionId connection, ulong accountId, ulong sessionId);

    /// <summary>A session closed (disconnect or drop).</summary>
    void OnSessionClosed(ConnectionId connection, ulong accountId);
}
