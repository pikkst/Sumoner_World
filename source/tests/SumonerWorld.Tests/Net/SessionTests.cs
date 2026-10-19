using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Core.Randomness;
using SumonerWorld.Net.Messages;
using SumonerWorld.Net.Sessions;
using SumonerWorld.Net.Transport;

namespace SumonerWorld.Tests.Net;

public class SessionTests
{
    private const string Secret = "test-secret";

    [Fact]
    public void A_valid_dev_ticket_opens_a_session()
    {
        var net = new Lan(maxPlayers: 4);
        ClientEndpoint client = net.Join(DevTokenAuthenticator.Ticket(1001, Secret));

        net.Run(steps: 5);

        Assert.Equal(ClientSessionState.InSession, client.State);
        Assert.Equal(1001UL, client.Session!.Value.AccountId);
        Assert.NotEqual(0UL, client.Session.Value.SessionToken);
        Assert.Equal(1, net.Server.PlayerCount);
        Assert.Contains(net.Log.Lines, line => line.StartsWith("Info session.welcome", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("dev:1001:wrong-secret")]
    [InlineData("dev:0:test-secret")]
    [InlineData("dev:abc:test-secret")]
    [InlineData("steam:1234")]
    [InlineData("")]
    public void Bad_tickets_are_rejected_and_disconnected(string ticket)
    {
        var net = new Lan(maxPlayers: 4);
        ClientEndpoint client = net.Join(ticket);

        net.Run(steps: 8);

        Assert.Equal(ClientSessionState.Rejected, client.State);
        Assert.Equal(RejectReason.NotAuthenticated, client.RejectReason);
        Assert.Equal(0, net.Server.PlayerCount);
    }

    [Fact]
    public void A_full_server_rejects_the_next_player()
    {
        var net = new Lan(maxPlayers: 1);
        ClientEndpoint first = net.Join(DevTokenAuthenticator.Ticket(1, Secret));
        net.Run(steps: 5);
        ClientEndpoint second = net.Join(DevTokenAuthenticator.Ticket(2, Secret));

        net.Run(steps: 8);

        Assert.Equal(ClientSessionState.InSession, first.State);
        Assert.Equal(RejectReason.ServerFull, second.RejectReason);
    }

    [Fact]
    public void Talking_before_hello_or_sending_garbage_gets_the_connection_dropped()
    {
        var net = new Lan(maxPlayers: 4);
        LoopbackTransport rogue = net.Network.CreateEndpoint(16);
        ConnectionId link = rogue.Connect(net.ServerTransport);
        net.Run(steps: 3);
        var buffer = new byte[64];
        var writer = new MessageWriter(buffer);
        InputBatch.Write(ref writer, [new PlayerInput(1, 0, 0, 0)]);
        rogue.Send(link, Channel.ReliableOrdered, writer.WrittenSpan);

        net.Run(steps: 3);

        Assert.Equal(0, net.Server.PlayerCount);
        Assert.Contains(net.Log.Lines, line => line.Contains("Expected Hello first", StringComparison.Ordinal));
    }

    [Fact]
    public void Disconnecting_suspends_the_session_until_the_reconnect_window_ends()
    {
        var net = new Lan(maxPlayers: 4);
        net.Join(DevTokenAuthenticator.Ticket(1001, Secret));
        net.Run(steps: 5);

        net.Clients[0].Transport.Disconnect(net.Clients[0].Link);
        net.Run(steps: 3);
        Assert.Equal((0, 1), (net.Server.PlayerCount, net.Server.SuspendedCount));

        net.Server.Update(ServerEndpoint.ReconnectWindowTicks);
        Assert.Equal(1, net.Server.SuspendedCount);
        net.Server.Update(ServerEndpoint.ReconnectWindowTicks + 1);

        Assert.Equal(0, net.Server.SuspendedCount);
        Assert.Contains(net.Log.Lines, line => line.StartsWith("Info session.expired", StringComparison.Ordinal));
    }

    [Fact]
    public void Reconnecting_with_the_token_resumes_the_same_session()
    {
        var net = new Lan(maxPlayers: 4);
        ClientEndpoint client = net.Join(DevTokenAuthenticator.Ticket(1001, Secret));
        net.Run(steps: 5);
        ulong token = client.Session!.Value.SessionToken;

        net.Reconnect(0);
        net.Run(steps: 6);

        Assert.Equal(ClientSessionState.InSession, client.State);
        Assert.True(client.Session.Value.Resumed);
        Assert.Equal(token, client.Session.Value.SessionToken);
        Assert.Equal((1, 0), (net.Server.PlayerCount, net.Server.SuspendedCount));
    }

    [Fact]
    public void An_expired_or_foreign_token_starts_a_new_session()
    {
        var net = new Lan(maxPlayers: 4);
        ClientEndpoint client = net.Join(DevTokenAuthenticator.Ticket(1001, Secret));
        net.Run(steps: 5);
        ulong token = client.Session!.Value.SessionToken;
        net.Clients[0].Transport.Disconnect(net.Clients[0].Link);
        net.Run(steps: 3);
        net.Server.Update(ServerEndpoint.ReconnectWindowTicks + 10);

        net.Reconnect(0);
        net.Run(steps: 6);
        Assert.False(client.Session.Value.Resumed);
        Assert.NotEqual(token, client.Session.Value.SessionToken);

        var thief = new ClientEndpoint(net.Network.CreateEndpoint(16), net.Log, new Hello(client.Session.Value.SessionToken, DevTokenAuthenticator.Ticket(2002, Secret)));
        net.Add(thief);
        net.Run(steps: 6);

        Assert.False(thief.Session!.Value.Resumed);
        Assert.Equal(2, net.Server.PlayerCount);
    }

    [Fact]
    public void Logging_in_again_without_the_token_replaces_the_old_session()
    {
        var net = new Lan(maxPlayers: 4);
        ClientEndpoint first = net.Join(DevTokenAuthenticator.Ticket(1001, Secret));
        net.Run(steps: 5);

        ClientEndpoint second = net.Join(DevTokenAuthenticator.Ticket(1001, Secret));
        net.Run(steps: 6);

        Assert.Equal(ClientSessionState.Disconnected, first.State);
        Assert.Equal(ClientSessionState.InSession, second.State);
        Assert.Equal((1, 0), (net.Server.PlayerCount, net.Server.SuspendedCount));
        Assert.Contains(net.Log.Lines, line => line.StartsWith("Info session.replaced", StringComparison.Ordinal));
    }

    private sealed class Lan
    {
        public Lan(int maxPlayers)
        {
            Network = new LoopbackNetwork(new SeededRandom(1), latencySteps: 1, unreliableLossPercent: 0, bufferCount: 64);
            ServerTransport = Network.CreateEndpoint(64);
            Server = new ServerEndpoint(ServerTransport, new DevTokenAuthenticator(Secret), Log, maxPlayers);
        }

        public LoopbackNetwork Network { get; }

        public LoopbackTransport ServerTransport { get; }

        public ServerEndpoint Server { get; }

        public RecordingLog Log { get; } = new();

        public List<(LoopbackTransport Transport, ConnectionId Link, ClientEndpoint Endpoint)> Clients { get; } = [];

        public ClientEndpoint Join(string ticket) => Add(new ClientEndpoint(Network.CreateEndpoint(16), Log, new Hello(0, ticket)));

        public ClientEndpoint Add(ClientEndpoint endpoint)
        {
            LoopbackTransport transport = Network.CreateEndpoint(16);
            endpoint.Rebind(transport);
            ConnectionId link = transport.Connect(ServerTransport);
            Clients.Add((transport, link, endpoint));
            return endpoint;
        }

        public void Reconnect(int client)
        {
            (LoopbackTransport transport, ConnectionId link, ClientEndpoint endpoint) = Clients[client];
            transport.Disconnect(link);
            endpoint.Rebind(transport);
            Clients[client] = (transport, transport.Connect(ServerTransport), endpoint);
        }

        public void Run(int steps)
        {
            for (int i = 0; i < steps; i++)
            {
                Network.Step();
                ServerTransport.Poll(Server);
                foreach ((LoopbackTransport transport, _, ClientEndpoint endpoint) in Clients)
                {
                    transport.Poll(endpoint);
                }
            }
        }
    }

    private sealed class RecordingLog : ILog
    {
        public List<string> Lines { get; } = [];

        public bool IsEnabled(LogLevel level) => true;

        public void Write(LogLevel level, string eventName, string message) => Lines.Add($"{level} {eventName} {message}");
    }
}
