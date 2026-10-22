using SumonerWorld.Core.Academy;
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

    private GameClient Join(ulong account)
    {
        LoopbackTransport transport = _network.CreateEndpoint(1024);
        var client = new GameClient(transport, NullLog.Instance, new Hello(0, DevTokenAuthenticator.Ticket(account, Secret)), new GameServerSettings().Movement);
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
