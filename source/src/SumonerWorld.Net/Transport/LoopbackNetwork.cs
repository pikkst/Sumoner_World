using SumonerWorld.Core.Randomness;

namespace SumonerWorld.Net.Transport;

/// <summary>
/// An in-process network for tests, bots (M1-18) and listen-server co-op: endpoints exchange messages through preallocated buffers
/// with a fixed latency in steps and a seeded loss rate for unreliable messages (reliable ones always arrive, in order). Time moves
/// only when <see cref="Step"/> is called, so runs are repeatable (R11). No allocation after construction (R21).
/// </summary>
public sealed class LoopbackNetwork
{
    /// <summary>Largest payload, in bytes (safe for one UDP datagram).</summary>
    public const int MaxPayload = 1200;

    private const int PercentRange = 100;

    private readonly IRandom _random;
    private readonly int _latencySteps;
    private readonly int _unreliableLossPercent;
    private readonly byte[] _payloads;
    private readonly int[] _freeBuffers;
    private readonly List<LoopbackTransport> _endpoints = [];
    private int _freeCount;

    /// <summary>Creates a network. <paramref name="bufferCount"/> messages can be in flight at once.</summary>
    public LoopbackNetwork(IRandom random, int latencySteps, int unreliableLossPercent, int bufferCount)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentOutOfRangeException.ThrowIfNegative(latencySteps);
        ArgumentOutOfRangeException.ThrowIfNegative(unreliableLossPercent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(unreliableLossPercent, PercentRange);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferCount);

        _random = random;
        _latencySteps = latencySteps;
        _unreliableLossPercent = unreliableLossPercent;
        _payloads = new byte[bufferCount * MaxPayload];
        _freeBuffers = new int[bufferCount];
        for (int i = 0; i < bufferCount; i++)
        {
            _freeBuffers[i] = bufferCount - 1 - i;
        }

        _freeCount = bufferCount;
    }

    /// <summary>Current network time, in steps.</summary>
    public long CurrentStep { get; private set; }

    /// <summary>Unreliable messages dropped so far (simulated loss).</summary>
    public int Dropped { get; private set; }

    /// <summary>Adds an endpoint (a server or a client). <paramref name="queueCapacity"/> messages can wait in its inbox.</summary>
    public LoopbackTransport CreateEndpoint(int queueCapacity)
    {
        var endpoint = new LoopbackTransport(this, queueCapacity);
        _endpoints.Add(endpoint);
        return endpoint;
    }

    /// <summary>Advances network time by one step (call once per simulation tick).</summary>
    public void Step() => CurrentStep++;

    internal long DeliveryStep => CurrentStep + _latencySteps;

    internal bool ShouldDrop(Channel channel)
    {
        if (channel == Channel.ReliableOrdered || _unreliableLossPercent == 0)
        {
            return false;
        }

        bool drop = _random.NextInt(0, PercentRange) < _unreliableLossPercent;
        Dropped += drop ? 1 : 0;
        return drop;
    }

    internal int RentBuffer(ReadOnlySpan<byte> payload)
    {
        if (payload.Length > MaxPayload)
        {
            throw new ArgumentException($"Payload of {payload.Length} bytes exceeds the {MaxPayload}-byte limit.", nameof(payload));
        }

        if (_freeCount == 0)
        {
            throw new InvalidOperationException("Loopback network buffers exhausted: poll the endpoints every step or raise the buffer count.");
        }

        int buffer = _freeBuffers[--_freeCount];
        payload.CopyTo(_payloads.AsSpan(buffer * MaxPayload, MaxPayload));
        return buffer;
    }

    internal ReadOnlySpan<byte> Payload(int buffer, int length) => _payloads.AsSpan(buffer * MaxPayload, length);

    internal void ReturnBuffer(int buffer) => _freeBuffers[_freeCount++] = buffer;
}

/// <summary>One endpoint of a <see cref="LoopbackNetwork"/>.</summary>
public sealed class LoopbackTransport : ITransport
{
    private const int NoBuffer = -1;

    private readonly LoopbackNetwork _network;
    private readonly Packet[] _inbox;
    private readonly List<Link> _links = [];
    private int _head;
    private int _count;

    internal LoopbackTransport(LoopbackNetwork network, int queueCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(queueCapacity);
        _network = network;
        _inbox = new Packet[queueCapacity];
    }

    private enum PacketKind : byte
    {
        Connect,
        Data,
        Disconnect,
    }

    /// <inheritdoc />
    public int MaxPayload => LoopbackNetwork.MaxPayload;

    /// <summary>Opens a connection to <paramref name="server"/>; both sides get <see cref="ITransportHandler.OnConnected"/> after the latency.</summary>
    public ConnectionId Connect(LoopbackTransport server)
    {
        ArgumentNullException.ThrowIfNull(server);
        ConnectionId local = AddLink(server);
        ConnectionId remote = server.AddLink(this);
        _links[local.Value] = _links[local.Value] with { RemoteId = remote };
        server._links[remote.Value] = server._links[remote.Value] with { RemoteId = local };

        Enqueue(new Packet(local, Channel.ReliableOrdered, PacketKind.Connect, NoBuffer, 0, _network.DeliveryStep));
        server.Enqueue(new Packet(remote, Channel.ReliableOrdered, PacketKind.Connect, NoBuffer, 0, _network.DeliveryStep));
        return local;
    }

    /// <inheritdoc />
    public void Send(ConnectionId connection, Channel channel, ReadOnlySpan<byte> payload)
    {
        if (!IsOpen(connection) || _network.ShouldDrop(channel))
        {
            return;
        }

        Link link = _links[connection.Value];
        int buffer = _network.RentBuffer(payload);
        link.Remote.Enqueue(new Packet(link.RemoteId, channel, PacketKind.Data, buffer, payload.Length, _network.DeliveryStep));
    }

    /// <inheritdoc />
    public void Disconnect(ConnectionId connection)
    {
        if (!IsOpen(connection))
        {
            return;
        }

        Link link = _links[connection.Value];
        // Graceful close: the other side keeps receiving what was sent before and closes when the disconnect arrives.
        _links[connection.Value] = link with { Open = false };
        link.Remote.Enqueue(new Packet(link.RemoteId, Channel.ReliableOrdered, PacketKind.Disconnect, NoBuffer, 0, _network.DeliveryStep));
    }

    /// <inheritdoc />
    public void Poll(ITransportHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        while (_count > 0 && _inbox[_head].DeliverAt <= _network.CurrentStep)
        {
            Packet packet = _inbox[_head];
            _head = (_head + 1) % _inbox.Length;
            _count--;
            switch (packet.Kind)
            {
                case PacketKind.Connect:
                    handler.OnConnected(packet.Connection);
                    break;
                case PacketKind.Disconnect:
                    _links[packet.Connection.Value] = _links[packet.Connection.Value] with { Open = false };
                    handler.OnDisconnected(packet.Connection, DisconnectReason.Remote);
                    break;
                default:
                    // Data still in flight when a connection closes is lost, as on a real network.
                    if (IsOpen(packet.Connection))
                    {
                        handler.OnData(packet.Connection, packet.Channel, _network.Payload(packet.Buffer, packet.Length));
                    }

                    _network.ReturnBuffer(packet.Buffer);
                    break;
            }
        }
    }

    private bool IsOpen(ConnectionId connection) =>
        (uint)connection.Value < (uint)_links.Count && _links[connection.Value].Open;

    private ConnectionId AddLink(LoopbackTransport remote)
    {
        _links.Add(new Link(remote, default, Open: true));
        return new ConnectionId(_links.Count - 1);
    }

    // Same latency for every message, so a FIFO queue keeps send order (reliable ordered delivery).
    private void Enqueue(Packet packet)
    {
        if (_count == _inbox.Length)
        {
            throw new InvalidOperationException("Loopback inbox full: poll every step or raise the queue capacity.");
        }

        _inbox[(_head + _count) % _inbox.Length] = packet;
        _count++;
    }

    private readonly record struct Link(LoopbackTransport Remote, ConnectionId RemoteId, bool Open);

    private readonly record struct Packet(ConnectionId Connection, Channel Channel, PacketKind Kind, int Buffer, int Length, long DeliverAt);
}
