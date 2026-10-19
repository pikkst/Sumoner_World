using SumonerWorld.Core.Randomness;
using SumonerWorld.Net.Transport;

namespace SumonerWorld.Tests.Net;

[Collection(AllocationMeasurements.Name)]
public class LoopbackNetworkTests
{
    [Fact]
    public void Connecting_sending_and_disconnecting_arrive_after_the_latency()
    {
        var network = new LoopbackNetwork(new SeededRandom(1), latencySteps: 3, unreliableLossPercent: 0, bufferCount: 32);
        LoopbackTransport server = network.CreateEndpoint(32);
        LoopbackTransport client = network.CreateEndpoint(32);
        var serverLog = new Recorder();
        var clientLog = new Recorder();

        ConnectionId toServer = client.Connect(server);
        client.Send(toServer, Channel.ReliableOrdered, [1, 2, 3]);
        Pump(network, 2, server, serverLog, client, clientLog);
        Assert.Empty(serverLog.Events);

        Pump(network, 1, server, serverLog, client, clientLog);
        Assert.Equal(["connected 0", "data 0 ReliableOrdered 1,2,3"], serverLog.Events);
        Assert.Equal(["connected 0"], clientLog.Events);

        client.Disconnect(toServer);
        client.Send(toServer, Channel.ReliableOrdered, [9]);
        Pump(network, 3, server, serverLog, client, clientLog);
        Assert.Equal("disconnected 0 Remote", serverLog.Events[^1]);
        Assert.Equal(3, serverLog.Events.Count);
    }

    [Fact]
    public void Reliable_messages_always_arrive_in_order_even_on_a_lossy_network()
    {
        var network = new LoopbackNetwork(new SeededRandom(7), latencySteps: 2, unreliableLossPercent: 50, bufferCount: 512);
        LoopbackTransport server = network.CreateEndpoint(512);
        LoopbackTransport client = network.CreateEndpoint(512);
        var serverLog = new Recorder();
        ConnectionId link = client.Connect(server);

        for (byte i = 0; i < 200; i++)
        {
            client.Send(link, Channel.ReliableOrdered, [i]);
            client.Send(link, Channel.Unreliable, [i]);
        }

        Pump(network, 3, server, serverLog, client, new Recorder());

        string[] reliable = [.. serverLog.Events.Where(e => e.Contains("ReliableOrdered", StringComparison.Ordinal))];
        int unreliable = serverLog.Events.Count(e => e.Contains(" Unreliable ", StringComparison.Ordinal));
        Assert.Equal(Enumerable.Range(0, 200).Select(i => $"data 0 ReliableOrdered {i}"), reliable);
        Assert.InRange(unreliable, 70, 130);
        Assert.Equal(200 - unreliable, network.Dropped);
    }

    [Fact]
    public void The_same_seed_drops_the_same_messages()
    {
        Assert.Equal(RunLossy(seed: 5), RunLossy(seed: 5));
        Assert.NotEqual(RunLossy(seed: 5), RunLossy(seed: 6));
    }

    [Fact]
    public void Oversized_payloads_and_full_queues_fail_loudly()
    {
        var network = new LoopbackNetwork(new SeededRandom(1), latencySteps: 0, unreliableLossPercent: 0, bufferCount: 2);
        LoopbackTransport server = network.CreateEndpoint(8);
        LoopbackTransport client = network.CreateEndpoint(8);
        ConnectionId link = client.Connect(server);

        Assert.Throws<ArgumentException>(() => client.Send(link, Channel.ReliableOrdered, new byte[LoopbackNetwork.MaxPayload + 1]));
        client.Send(link, Channel.ReliableOrdered, [1]);
        client.Send(link, Channel.ReliableOrdered, [2]);
        Assert.Throws<InvalidOperationException>(() => client.Send(link, Channel.ReliableOrdered, [3]));
    }

    [Fact]
    public void Steady_traffic_does_not_allocate()
    {
        var network = new LoopbackNetwork(new SeededRandom(3), latencySteps: 1, unreliableLossPercent: 10, bufferCount: 64);
        LoopbackTransport server = network.CreateEndpoint(64);
        LoopbackTransport client = network.CreateEndpoint(64);
        var counter = new Counter();
        ConnectionId link = client.Connect(server);
        byte[] payload = new byte[100];
        Pump(network, 2, server, counter, client, counter);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int step = 0; step < 5_000; step++)
        {
            client.Send(link, Channel.Unreliable, payload);
            client.Send(link, Channel.ReliableOrdered, payload);
            network.Step();
            server.Poll(counter);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(counter.Messages > 9_000);
    }

    private static string RunLossy(ulong seed)
    {
        var network = new LoopbackNetwork(new SeededRandom(seed), latencySteps: 0, unreliableLossPercent: 30, bufferCount: 128);
        LoopbackTransport server = network.CreateEndpoint(128);
        LoopbackTransport client = network.CreateEndpoint(128);
        var log = new Recorder();
        ConnectionId link = client.Connect(server);
        for (byte i = 0; i < 100; i++)
        {
            client.Send(link, Channel.Unreliable, [i]);
        }

        server.Poll(log);
        return string.Join(";", log.Events);
    }

    private static void Pump(LoopbackNetwork network, int steps, LoopbackTransport server, ITransportHandler serverLog, LoopbackTransport client, ITransportHandler clientLog)
    {
        for (int i = 0; i < steps; i++)
        {
            network.Step();
            server.Poll(serverLog);
            client.Poll(clientLog);
        }
    }

    private sealed class Recorder : ITransportHandler
    {
        public List<string> Events { get; } = [];

        public void OnConnected(ConnectionId connection) => Events.Add($"connected {connection.Value}");

        public void OnDisconnected(ConnectionId connection, DisconnectReason reason) => Events.Add($"disconnected {connection.Value} {reason}");

        public void OnData(ConnectionId connection, Channel channel, ReadOnlySpan<byte> payload) =>
            Events.Add($"data {connection.Value} {channel} {string.Join(",", payload.ToArray())}");
    }

    private sealed class Counter : ITransportHandler
    {
        public int Messages { get; private set; }

        public void OnConnected(ConnectionId connection)
        {
        }

        public void OnDisconnected(ConnectionId connection, DisconnectReason reason)
        {
        }

        public void OnData(ConnectionId connection, Channel channel, ReadOnlySpan<byte> payload) => Messages++;
    }
}
