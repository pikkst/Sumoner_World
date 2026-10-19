namespace SumonerWorld.Net.Transport;

/// <summary>Delivery guarantee of a message (TDD §3.1).</summary>
public enum Channel : byte
{
    /// <summary>May be lost or arrive late; never retried. Snapshots and movement input.</summary>
    Unreliable,

    /// <summary>Always arrives, in send order. Intents, events, transactions, chat.</summary>
    ReliableOrdered,
}

/// <summary>Why a connection ended.</summary>
public enum DisconnectReason : byte
{
    /// <summary>This side closed it.</summary>
    Local,

    /// <summary>The other side closed it.</summary>
    Remote,

    /// <summary>No traffic for too long.</summary>
    Timeout,
}

/// <summary>One connection as seen from one endpoint. Ids are per endpoint and never reused while the endpoint lives.</summary>
/// <param name="Value">The id.</param>
public readonly record struct ConnectionId(int Value);

/// <summary>Receives what <see cref="ITransport.Poll"/> delivers, on the calling (simulation) thread.</summary>
public interface ITransportHandler
{
    /// <summary>A connection was established (incoming on servers, the outgoing one on clients).</summary>
    void OnConnected(ConnectionId connection);

    /// <summary>A connection ended.</summary>
    void OnDisconnected(ConnectionId connection, DisconnectReason reason);

    /// <summary>A message arrived. <paramref name="payload"/> is only valid during the call.</summary>
    void OnData(ConnectionId connection, Channel channel, ReadOnlySpan<byte> payload);
}

/// <summary>
/// Message transport (TDD §3.1). Implementations: <see cref="LoopbackTransport"/> (in-process, for tests and bots) and the Godot
/// ENet adapter in the Game project; Steam Networking Sockets can be added later. Sending never blocks; everything received is
/// handed to the handler in <see cref="Poll"/>.
/// </summary>
public interface ITransport
{
    /// <summary>Largest payload one message may carry, in bytes.</summary>
    int MaxPayload { get; }

    /// <summary>Delivers everything that has arrived to <paramref name="handler"/>.</summary>
    void Poll(ITransportHandler handler);

    /// <summary>Queues <paramref name="payload"/> for <paramref name="connection"/>. Unknown or closed connections are ignored.</summary>
    void Send(ConnectionId connection, Channel channel, ReadOnlySpan<byte> payload);

    /// <summary>Closes a connection; the other side is told.</summary>
    void Disconnect(ConnectionId connection);
}
