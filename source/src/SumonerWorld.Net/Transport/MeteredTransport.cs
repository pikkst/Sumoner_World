namespace SumonerWorld.Net.Transport;

/// <summary>Payload bytes and messages in each direction.</summary>
/// <param name="BytesSent">Payload bytes sent.</param>
/// <param name="MessagesSent">Messages sent.</param>
/// <param name="BytesReceived">Payload bytes received.</param>
/// <param name="MessagesReceived">Messages received.</param>
public readonly record struct TrafficCounters(long BytesSent, long MessagesSent, long BytesReceived, long MessagesReceived)
{
    /// <summary>Upper bound of the per-message cost below the payload: IPv4 + UDP (28 B) + ENet protocol and command headers (16 B),
    /// assuming one datagram per message (ENet often packs several, so real wire traffic is lower).</summary>
    public const int OverheadPerMessage = 44;

    /// <summary>Estimated wire bytes sent (payload + <see cref="OverheadPerMessage"/> per message).</summary>
    public long WireBytesSent => BytesSent + (MessagesSent * OverheadPerMessage);

    /// <summary>Estimated wire bytes received.</summary>
    public long WireBytesReceived => BytesReceived + (MessagesReceived * OverheadPerMessage);
}

/// <summary>
/// Counts what passes through another <see cref="ITransport"/>, per connection and in total (TDD §3.2 bandwidth budget, B2 metric
/// "bytes sent/received per client per second"). Sent bytes are counted when queued, so messages the network loses still count.
/// Allocates only when a new connection id appears.
/// </summary>
public sealed class MeteredTransport : ITransport
{
    private readonly ITransport _inner;
    private readonly CountingHandler _counting;
    private readonly List<Counter> _connections = [];
    private Counter _total;

    /// <summary>Wraps <paramref name="inner"/>.</summary>
    public MeteredTransport(ITransport inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
        _counting = new CountingHandler(this);
    }

    /// <inheritdoc />
    public int MaxPayload => _inner.MaxPayload;

    /// <summary>Everything since the start.</summary>
    public TrafficCounters Total => _total.ToCounters();

    /// <summary>Highest connection id seen + 1.</summary>
    public int ConnectionSlots => _connections.Count;

    /// <summary>Everything sent to and received from one connection since it opened.</summary>
    public TrafficCounters For(ConnectionId connection) =>
        (uint)connection.Value < (uint)_connections.Count ? _connections[connection.Value].ToCounters() : default;

    /// <inheritdoc />
    public void Poll(ITransportHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _counting.Target = handler;
        try
        {
            _inner.Poll(_counting);
        }
        finally
        {
            _counting.Target = null;
        }
    }

    /// <inheritdoc />
    public void Send(ConnectionId connection, Channel channel, ReadOnlySpan<byte> payload)
    {
        Slot(connection).Sent(payload.Length);
        _total.Sent(payload.Length);
        _inner.Send(connection, channel, payload);
    }

    /// <inheritdoc />
    public void Disconnect(ConnectionId connection) => _inner.Disconnect(connection);

    private ref Counter Slot(ConnectionId connection)
    {
        while (_connections.Count <= connection.Value)
        {
            _connections.Add(default);
        }

        return ref System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_connections)[connection.Value];
    }

    private struct Counter
    {
        private long _bytesSent;
        private long _messagesSent;
        private long _bytesReceived;
        private long _messagesReceived;

        public void Sent(int bytes)
        {
            _bytesSent += bytes;
            _messagesSent++;
        }

        public void Received(int bytes)
        {
            _bytesReceived += bytes;
            _messagesReceived++;
        }

        public readonly TrafficCounters ToCounters() => new(_bytesSent, _messagesSent, _bytesReceived, _messagesReceived);
    }

    private sealed class CountingHandler(MeteredTransport owner) : ITransportHandler
    {
        public ITransportHandler? Target { get; set; }

        public void OnConnected(ConnectionId connection)
        {
            owner.Slot(connection);
            Target!.OnConnected(connection);
        }

        public void OnDisconnected(ConnectionId connection, DisconnectReason reason) => Target!.OnDisconnected(connection, reason);

        public void OnData(ConnectionId connection, Channel channel, ReadOnlySpan<byte> payload)
        {
            owner.Slot(connection).Received(payload.Length);
            owner._total.Received(payload.Length);
            Target!.OnData(connection, channel, payload);
        }
    }
}
