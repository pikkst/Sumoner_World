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
    public void Disconnecting_closes_the_session()
    {
        var net = new Lan(maxPlayers: 4);
        net.Join(DevTokenAuthenticator.Ticket(1001, Secret));
        net.Run(steps: 5);

        net.Clients[0].Transport.Disconnect(net.Clients[0].Link);
        net.Run(steps: 3);

        Assert.Equal(0, net.Server.PlayerCount);
        Assert.Contains(net.Log.Lines, line => line.StartsWith("Info session.closed", StringComparison.Ordinal));
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

        public ClientEndpoint Join(string ticket)
        {
            LoopbackTransport transport = Network.CreateEndpoint(16);
            var endpoint = new ClientEndpoint(transport, Log, new Hello(0, ticket));
            ConnectionId link = transport.Connect(ServerTransport);
            Clients.Add((transport, link, endpoint));
            return endpoint;
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
