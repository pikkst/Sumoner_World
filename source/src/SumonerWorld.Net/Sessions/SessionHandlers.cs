using SumonerWorld.Net.Messages;
using SumonerWorld.Net.Transport;

namespace SumonerWorld.Net.Sessions;

/// <summary>Receives the messages that follow the handshake, after the endpoint has checked the header.</summary>
public interface ISessionMessageHandler
{
    /// <summary>A message of <paramref name="type"/> arrived; <paramref name="reader"/> is positioned after the header.</summary>
    void OnMessage(ConnectionId connection, MessageType type, ref MessageReader reader);
}

/// <summary>The game side of the client's session: told about every <see cref="Welcome"/> (first join and each reconnect).</summary>
public interface IClientSessionHandler : ISessionMessageHandler
{
    /// <summary>The server opened or resumed the session (see <see cref="Welcome.Resumed"/>).</summary>
    void OnWelcome(in Welcome welcome);
}

/// <summary>
/// The game side of the server's sessions (TDD §3.5): opened → (suspended → resumed)* → closed. A suspended session's player is
/// disconnected but may come back within the reconnect window; closed is final.
/// </summary>
public interface IServerSessionHandler : ISessionMessageHandler
{
    /// <summary>A new session opened. <paramref name="sessionId"/> is server-assigned, unique for the server's life and not secret.</summary>
    void OnSessionOpened(ConnectionId connection, ulong accountId, ulong sessionId);

    /// <summary>The session's connection was lost; it can still be resumed.</summary>
    void OnSessionSuspended(ulong sessionId);

    /// <summary>A suspended (or silently replaced) session continues on a new connection.</summary>
    void OnSessionResumed(ConnectionId connection, ulong sessionId);

    /// <summary>The session ended for good (expired, replaced by a new login or dropped for a violation).</summary>
    void OnSessionClosed(ulong sessionId);
}
