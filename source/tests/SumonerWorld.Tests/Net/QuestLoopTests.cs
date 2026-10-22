using SumonerWorld.Core.Academy;
using SumonerWorld.Core.Data;
using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Core.Movement;
using SumonerWorld.Core.Randomness;
using SumonerWorld.Core.World;
using SumonerWorld.Net.Client;
using SumonerWorld.Net.Messages;
using SumonerWorld.Net.Server;
using SumonerWorld.Net.Sessions;
using SumonerWorld.Net.Transport;
using SumonerWorld.Scenarios;

namespace SumonerWorld.Tests.Net;

// Quests and the Academy through the real server loop: events from intents must reach the quest system in the same tick.
public sealed class QuestLoopTests : IDisposable
{
    private const string Secret = "quest-loop-secret";

    private readonly LoopbackNetwork _network = new(new SeededRandom(0x0E57), latencySteps: 2, unreliableLossPercent: 0, bufferCount: 4096);
    private readonly LoopbackTransport _serverTransport;
    private readonly CreatureWorld _world = new(ShippedData.Current, new CreatureWorldSettings(), seed: 12);
    private readonly GameServer _server;
    private readonly List<GameClient> _clients = [];

    public QuestLoopTests()
    {
        _serverTransport = _network.CreateEndpoint(2048);
        _server = new GameServer(_world, _serverTransport, new DevTokenAuthenticator(Secret), NullLog.Instance, new GameServerSettings());
    }

    public void Dispose() => _world.Dispose();

    [Fact]
    public void A_new_player_gets_the_ranking_quest_and_talking_to_the_headmaster_counts()
    {
        GameClient alice = Join(1001);
        Run(60);
        int ranking = RankingRules.RankingQuest(_world.Data);
        Assert.Equal(0, alice.Quests.Single(entry => entry.Quest == ranking).Step);

        uint talk = alice.SendIntent(IntentKind.Talk, (ulong)RankingRules.NpcIndex(_world.Data, "headmaster"), 0);
        Run(60);

        Assert.True(alice.IntentResults[talk].IsApplied, alice.IntentResults[talk].ToString());
        Assert.Equal(1, alice.Quests.Single(entry => entry.Quest == ranking).Step);
    }

    [Fact]
    public void A_client_with_other_game_data_is_refused_and_one_with_the_same_data_is_welcomed()
    {
        GameClient old = Join(1001, fingerprint: _world.Data.Fingerprint ^ 1);
        GameClient current = Join(1002, fingerprint: _world.Data.Fingerprint);
        Run(30);

        Assert.NotEqual(0UL, _world.Data.Fingerprint);
        Assert.Equal((SumonerWorld.Net.Sessions.ClientSessionState.Rejected, RejectReason.DataMismatch), (old.Session.State, old.Session.RejectReason));
        Assert.Equal(SumonerWorld.Net.Sessions.ClientSessionState.InSession, current.Session.State);
    }

    [Fact]
    public void The_data_fingerprint_ignores_line_endings_and_file_order_but_not_content()
    {
        DataFile a = new("a.json", "{\n \"x\": 1\n}");
        DataFile b = new("dir/b.json", "{ }");
        ulong fingerprint = DataRegistryLoader.Fingerprint([a, b]);

        Assert.Equal(fingerprint, DataRegistryLoader.Fingerprint([b, a with { Content = a.Content.Replace("\n", "\r\n", StringComparison.Ordinal) }]));
        Assert.Equal(fingerprint, DataRegistryLoader.Fingerprint([a, b with { Path = "dir\\b.json" }]));
        Assert.NotEqual(fingerprint, DataRegistryLoader.Fingerprint([a with { Content = "{ \"x\": 2 }" }, b]));
    }

    private GameClient Join(ulong account, ulong fingerprint = 0)
    {
        LoopbackTransport transport = _network.CreateEndpoint(1024);
        var client = new GameClient(transport, NullLog.Instance, new Hello(0, DevTokenAuthenticator.Ticket(account, Secret), fingerprint), new GameServerSettings().Movement);
        transport.Connect(_serverTransport);
        _clients.Add(client);
        return client;
    }

    private void Run(int steps)
    {
        for (int i = 0; i < steps; i++)
        {
            _network.Step();
            _server.Tick();
            foreach (GameClient client in _clients)
            {
                client.Tick(MoveInput.None);
            }
        }
    }

    private sealed class NullLog : ILog
    {
        public static readonly NullLog Instance = new();

        public bool IsEnabled(LogLevel level) => false;

        public void Write(LogLevel level, string eventName, string message)
        {
        }
    }
}
