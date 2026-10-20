using Godot;
using SumonerWorld.Net.Transport;

namespace SumonerWorld.Game.Net;

/// <summary>
/// <see cref="ITransport"/> over Godot's <see cref="ENetConnection"/> at packet level (TDD §3.1). ENet channel 0 is unreliable,
/// channel 1 reliable and ordered. Godot's API copies packets into managed arrays, so this adapter allocates per packet; it lives
/// in the Game layer, outside the allocation-free simulation (R21), and can be replaced by Steam sockets behind the same interface.
/// </summary>
public sealed class GodotENetTransport : ITransport, IDisposable
{
    private const int ChannelCount = 2;
    private const int UnreliableChannel = 0;
    private const int ReliableChannel = 1;

    private readonly ENetConnection _connection;
    private readonly List<ENetPacketPeer?> _peers = [];
    private readonly Dictionary<ENetPacketPeer, int> _ids = [];

    private GodotENetTransport(ENetConnection connection)
    {
        _connection = connection;
    }

    /// <inheritdoc />
    public int MaxPayload => SumonerWorld.Net.Messages.Protocol.MaxMessageSize;

    /// <summary>Starts a server listening on all interfaces at <paramref name="port"/>.</summary>
    public static GodotENetTransport Listen(int port, int maxPeers)
    {
        var connection = new ENetConnection();
        Error error = connection.CreateHostBound("*", port, maxPeers, ChannelCount);
        if (error != Error.Ok)
        {
            throw new InvalidOperationException($"Could not listen on UDP port {port}: {error}.");
        }

        return new GodotENetTransport(connection);
    }

    /// <summary>Starts a client and begins connecting to <paramref name="address"/>:<paramref name="port"/>.</summary>
    public static GodotENetTransport Connect(string address, int port, out ConnectionId server)
    {
        var connection = new ENetConnection();
        Error error = connection.CreateHost(1, ChannelCount);
        if (error != Error.Ok)
        {
            throw new InvalidOperationException($"Could not create the ENet client: {error}.");
        }

        var transport = new GodotENetTransport(connection);
        ENetPacketPeer peer = connection.ConnectToHost(address, port, ChannelCount)
            ?? throw new InvalidOperationException($"Could not start connecting to {address}:{port}.");
        server = new ConnectionId(transport.Register(peer));
        return transport;
    }

    /// <inheritdoc />
    public void Poll(ITransportHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        while (true)
        {
            Godot.Collections.Array result = _connection.Service(0);
            var type = (ENetConnection.EventType)(int)result[0];
            var peer = result[1].As<ENetPacketPeer>();
            switch (type)
            {
                case ENetConnection.EventType.None:
                    return;
                case ENetConnection.EventType.Connect:
                    handler.OnConnected(new ConnectionId(Register(peer)));
                    break;
                case ENetConnection.EventType.Disconnect:
                    if (_ids.Remove(peer, out int closed))
                    {
                        _peers[closed] = null;
                        handler.OnDisconnected(new ConnectionId(closed), DisconnectReason.Remote);
                    }

                    break;
                case ENetConnection.EventType.Receive:
                    byte[] packet = peer.GetPacket();
                    Channel channel = (int)result[3] == ReliableChannel ? Channel.ReliableOrdered : Channel.Unreliable;
                    handler.OnData(new ConnectionId(Register(peer)), channel, packet);
                    break;
                default:
                    throw new InvalidOperationException($"ENet service failed ({type}).");
            }
        }
    }

    /// <inheritdoc />
    public void Send(ConnectionId connection, Channel channel, ReadOnlySpan<byte> payload)
    {
        if (!TryPeer(connection, out ENetPacketPeer? peer))
        {
            return;
        }

        int enetChannel = channel == Channel.ReliableOrdered ? ReliableChannel : UnreliableChannel;
        int flags = (int)(channel == Channel.ReliableOrdered ? ENetPacketPeer.FlagReliable : ENetPacketPeer.FlagUnsequenced);
        peer!.Send(enetChannel, payload.ToArray(), flags);
    }

    /// <inheritdoc />
    public void Disconnect(ConnectionId connection)
    {
        if (TryPeer(connection, out ENetPacketPeer? peer))
        {
            peer!.PeerDisconnectLater();
            _ids.Remove(peer);
            _peers[connection.Value] = null;
        }
    }

    /// <summary>Tells every connected peer goodbye at once (server shutdown), so clients do not wait for a timeout.</summary>
    public void DisconnectAll()
    {
        foreach (ENetPacketPeer? peer in _peers)
        {
            peer?.PeerDisconnectNow();
        }

        _peers.Clear();
        _ids.Clear();
        _connection.Flush();
    }

    /// <summary>Sends everything queued now instead of on the next poll.</summary>
    public void Flush() => _connection.Flush();

    /// <summary>Closes the host.</summary>
    public void Dispose()
    {
        _connection.Destroy();
        _connection.Dispose();
    }

    private int Register(ENetPacketPeer peer)
    {
        if (_ids.TryGetValue(peer, out int id))
        {
            return id;
        }

        _peers.Add(peer);
        id = _peers.Count - 1;
        _ids[peer] = id;
        return id;
    }

    private bool TryPeer(ConnectionId connection, out ENetPacketPeer? peer)
    {
        peer = (uint)connection.Value < (uint)_peers.Count ? _peers[connection.Value] : null;
        return peer is not null;
    }
}
