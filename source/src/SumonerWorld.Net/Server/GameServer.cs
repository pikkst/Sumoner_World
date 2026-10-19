using System.Numerics;
using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Movement;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.Transactions;
using SumonerWorld.Core.World;
using SumonerWorld.Net.Messages;
using SumonerWorld.Net.Sessions;
using SumonerWorld.Net.Transport;

namespace SumonerWorld.Net.Server;

/// <summary>Settings of the authoritative server.</summary>
public sealed class GameServerSettings
{
    /// <summary>Most players at once.</summary>
    public int MaxPlayers { get; init; } = 16;

    /// <summary>Where new avatars appear.</summary>
    public Vector3 SpawnPoint { get; init; } = new(256, 0, 256);

    /// <summary>Entities farther than this from a player are not sent to them (TDD §3.2: 150 m).</summary>
    public float InterestRadius { get; init; } = 150f;

    /// <summary>Snapshots per second (TDD §3.2: 20 Hz).</summary>
    public int SnapshotsPerSecond { get; init; } = 20;

    /// <summary>Player movement; clients must use the same settings for prediction.</summary>
    public MovementSettings Movement { get; init; } = new();
}

/// <summary>
/// The authoritative server loop (TDD §3.2): poll the transport → run one simulation tick (player inputs at stage 1, the creature
/// systems, intents through the <see cref="ITransactionProcessor"/> at stage 9) → send snapshots. Clients send inputs and intents,
/// never state (R31). Sessions (TDD §3.5): an avatar appears when a session opens. When the connection is lost it stays in the world
/// for <see cref="LingerTicks"/> (no escape from danger by disconnecting), then is parked; resuming within the reconnect window puts
/// it back where it was, with the same intent dedupe window. When the session closes, the avatar leaves for good.
/// </summary>
public sealed class GameServer : IServerSessionHandler
{
    /// <summary>30 s at 30 Hz: how long a disconnected player's avatar stays in the world.</summary>
    public const ulong LingerTicks = 900;

    private readonly CreatureWorld _world;
    private readonly ITransport _transport;
    private readonly ILog _log;
    private readonly GameServerSettings _settings;
    private readonly ServerEndpoint _endpoint;
    private readonly SimulationScheduler _scheduler;
    private readonly IntentDeduplicator _dedupe = new();
    private readonly CooldownTracker _cooldowns = new();
    private readonly List<PlayerSlot> _players = [];
    private readonly Dictionary<ConnectionId, PlayerSlot> _byConnection = [];
    private readonly Dictionary<ulong, PlayerSlot> _bySession = [];
    private readonly PlayerInput[] _batch = new PlayerInput[InputBatch.MaxInputs];
    private readonly byte[] _buffer = new byte[Protocol.MaxMessageSize];
    private readonly IntentRequestCodec _intentCodec = new();
    private readonly IntentAckCodec _ackCodec = new();
    private readonly SnapshotSender _snapshots;

    /// <summary>Creates the server around a world.</summary>
    public GameServer(CreatureWorld world, ITransport transport, IAuthenticator authenticator, ILog log, GameServerSettings settings, IMetrics? metrics = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(settings);

        _world = world;
        _transport = transport;
        _log = log;
        _settings = settings;
        _endpoint = new ServerEndpoint(transport, authenticator, log, settings.MaxPlayers, this);
        Movement = new MovementModel(settings.Movement);
        Transactions = new TransactionProcessor(_dedupe);
        _snapshots = new SnapshotSender(world, transport, settings.InterestRadius);
        _scheduler = new SimulationScheduler(world.Clock, world.Events, metrics);
        _scheduler.Register(new PlayerInputSystem(this));
        world.CreateSystems().RegisterAll(_scheduler);
        _scheduler.Register(new IntentSystem(this));
    }

    /// <summary>The simulated world.</summary>
    public CreatureWorld World => _world;

    /// <summary>The session endpoint.</summary>
    public ServerEndpoint Endpoint => _endpoint;

    /// <summary>The movement model clients must predict with.</summary>
    public MovementModel Movement { get; }

    /// <summary>Every state-changing intent goes through this.</summary>
    public TransactionProcessor Transactions { get; }

    /// <summary>Players with a session, connected or not.</summary>
    public int PlayerCount => _players.Count;

    /// <summary>Players currently connected.</summary>
    public int ConnectedCount => _byConnection.Count;

    /// <summary>Snapshot parts sent since the start.</summary>
    public int SnapshotPartsSent => _snapshots.PartsSent;

    /// <summary>The avatar of an account in the world, if any.</summary>
    public bool TryGetAvatar(ulong accountId, out Entity avatar)
    {
        foreach (PlayerSlot player in _players)
        {
            if (player.Account == accountId && player.InWorld)
            {
                avatar = player.Avatar;
                return true;
            }
        }

        avatar = default;
        return false;
    }

    /// <summary>The last input sequence applied for an account (0 when none or unknown).</summary>
    public uint LastAppliedInput(ulong accountId)
    {
        foreach (PlayerSlot player in _players)
        {
            if (player.Account == accountId)
            {
                return player.Inputs.LastApplied;
            }
        }

        return 0;
    }

    /// <summary>Adds <paramref name="count"/> wild creatures of a species at seeded random spots (dev servers before M2 ecosystem).</summary>
    public void Populate(string speciesId, int count, int herds)
    {
        ushort species = _world.SpeciesIndex(speciesId);
        Vector3 size = _world.BoundsMax - _world.BoundsMin;
        for (int i = 0; i < count; i++)
        {
            var position = _world.BoundsMin + new Vector3(
                (float)(_world.Random.NextDouble() * size.X), 0, (float)(_world.Random.NextDouble() * size.Z));
            _world.SpawnCreature(species, position, Personality.Roll(_world.Data.Species[species], _world.Random), _world.Random.NextInt(0, herds));
        }
    }

    /// <summary>One server tick: receive, simulate, replicate. The host flushes the transport afterwards.</summary>
    public void Tick()
    {
        _transport.Poll(_endpoint);
        _scheduler.RunTick();
        ulong tick = _world.Clock.Current.Index;
        ParkLingeringAvatars(tick);
        _endpoint.Update(tick);
        if (IsSnapshotTick(tick))
        {
            foreach (PlayerSlot player in _players)
            {
                if (player.Connection is ConnectionId connection && player.InWorld)
                {
                    _snapshots.Send(connection, player.Avatar, player.Inputs.LastApplied, tick);
                }
            }
        }

        if (tick % IntentDeduplicator.MaxIdleTicks == 0)
        {
            _dedupe.Prune(tick);
        }
    }

    /// <inheritdoc />
    public void OnSessionOpened(ConnectionId connection, ulong accountId, ulong sessionId)
    {
        var player = new PlayerSlot(accountId, sessionId) { Connection = connection, LastPosition = _settings.SpawnPoint };
        Enter(player);
        _players.Add(player);
        _byConnection.Add(connection, player);
        _bySession.Add(sessionId, player);
    }

    /// <inheritdoc />
    public void OnSessionSuspended(ulong sessionId)
    {
        if (_bySession.TryGetValue(sessionId, out PlayerSlot? player) && player.Connection is ConnectionId connection)
        {
            _byConnection.Remove(connection);
            player.Connection = null;
            player.LingerUntil = _world.Clock.Current.Index + LingerTicks;
            player.Inputs.Reset();
            player.Intents.Clear();
        }
    }

    /// <inheritdoc />
    public void OnSessionResumed(ConnectionId connection, ulong sessionId)
    {
        if (!_bySession.TryGetValue(sessionId, out PlayerSlot? player))
        {
            return;
        }

        if (player.Connection is ConnectionId old)
        {
            _byConnection.Remove(old);
        }

        player.Connection = connection;
        _byConnection.Add(connection, player);
        player.Inputs.Reset();
        player.Intents.Clear();
        if (!player.InWorld)
        {
            Enter(player);
        }
    }

    /// <inheritdoc />
    public void OnSessionClosed(ulong sessionId)
    {
        if (!_bySession.Remove(sessionId, out PlayerSlot? player))
        {
            return;
        }

        if (player.Connection is ConnectionId connection)
        {
            _byConnection.Remove(connection);
        }

        if (player.InWorld)
        {
            Leave(player);
        }

        _players.Remove(player);
        _cooldowns.Forget(player.Account);
        _dedupe.Forget(player.Session);
    }

    /// <inheritdoc />
    public void OnMessage(ConnectionId connection, MessageType type, ref MessageReader reader)
    {
        if (!_byConnection.TryGetValue(connection, out PlayerSlot? player))
        {
            return;
        }

        switch (type)
        {
            case MessageType.InputBatch:
                int count = InputBatch.Read(ref reader, _batch);
                for (int i = 0; i < count; i++)
                {
                    player.Inputs.Offer(_batch[i]);
                }

                break;
            case MessageType.Intent:
                if (player.Intents.Count >= PlayerSlot.MaxQueuedIntents)
                {
                    throw new MalformedMessageException($"More than {PlayerSlot.MaxQueuedIntents} intents queued in one tick.");
                }

                player.Intents.Enqueue(_intentCodec.Read(ref reader));
                break;
            default:
                throw new MalformedMessageException($"{type} is not a client message.");
        }
    }

    private void Enter(PlayerSlot player)
    {
        player.Avatar = _world.SpawnPlayerAvatar(player.Account, player.LastPosition);
        player.InWorld = true;
    }

    private void Leave(PlayerSlot player)
    {
        player.LastPosition = _world.World.GetComponent<Position>(player.Avatar).Value;
        _world.DespawnPlayerAvatar(player.Avatar);
        player.InWorld = false;
    }

    private void ParkLingeringAvatars(ulong tick)
    {
        foreach (PlayerSlot player in _players)
        {
            if (player.Connection is null && player.InWorld && tick >= player.LingerUntil)
            {
                Leave(player);
            }
        }
    }

    private bool IsSnapshotTick(ulong tick)
    {
        ulong rate = (ulong)_settings.SnapshotsPerSecond;
        ulong ticksPerSecond = (ulong)SimulationSettings.DefaultTicksPerSecond;
        return tick > 0 && (tick * rate / ticksPerSecond) != ((tick - 1) * rate / ticksPerSecond);
    }

    private void ApplyInputs()
    {
        ComponentStore<Position> positions = _world.World.Store<Position>();
        ComponentStore<Facing> facings = _world.World.Store<Facing>();
        foreach (PlayerSlot player in _players)
        {
            if (player.Connection is null || !player.InWorld)
            {
                continue;
            }

            int steps = player.Inputs.Buffered > PlayerInputBuffer.CatchUpThreshold ? 2 : 1;
            for (int i = 0; i < steps && player.Inputs.TryTakeNext(out PlayerInput input); i++)
            {
                var move = new MoveInput(input.MoveX, input.MoveZ);
                ref Position position = ref positions.Get(player.Avatar);
                position = new Position(Movement.Advance(position.Value, move));
                Vector3 direction = MovementModel.Direction(move);
                if (direction != Vector3.Zero)
                {
                    facings.Get(player.Avatar) = new Facing(Vector3.Normalize(direction));
                }

                _world.Grid.Move(player.Avatar, position.Value);
            }
        }
    }

    private void ApplyIntents(ulong now)
    {
        foreach (PlayerSlot player in _players)
        {
            if (player.Connection is not ConnectionId connection)
            {
                continue;
            }

            while (player.Intents.TryDequeue(out IntentRequest request))
            {
                IntentResult result = Execute(player, request, now);
                var writer = new MessageWriter(_buffer);
                _ackCodec.Write(ref writer, new IntentAck(request.Sequence, result));
                _transport.Send(connection, Channel.ReliableOrdered, writer.WrittenSpan);
                if (!result.IsApplied && _log.IsEnabled(LogLevel.Debug))
                {
                    _log.Write(LogLevel.Debug, "intent.rejected", $"account {player.Account} intent {request.Sequence} {request.Kind}: {result.Reason}");
                }
            }
        }
    }

    private IntentResult Execute(PlayerSlot player, IntentRequest request, ulong now)
    {
        var id = new IntentId(player.Session, request.Sequence);
        switch (request.Kind)
        {
            case IntentKind.OfferPact:
                var offer = new OfferPactTransaction(_world, _cooldowns, player.Avatar, new StableId(request.Target), new PactTerms(request.Argument));
                return Transactions.Execute(id, ref offer, now);
            default:
                return IntentResult.Reject(IntentRejectReason.InvalidArguments);
        }
    }

    private sealed class PlayerSlot(ulong account, ulong session)
    {
        public const int MaxQueuedIntents = 32;

        public ulong Account { get; } = account;

        public ulong Session { get; } = session;

        public ConnectionId? Connection { get; set; }

        public Entity Avatar { get; set; }

        public bool InWorld { get; set; }

        public Vector3 LastPosition { get; set; }

        public ulong LingerUntil { get; set; }

        public PlayerInputBuffer Inputs { get; } = new();

        public Queue<IntentRequest> Intents { get; } = new(MaxQueuedIntents);
    }

    private sealed class PlayerInputSystem(GameServer server) : ISimulationSystem
    {
        public SimulationStage Stage => SimulationStage.ApplyInputs;

        public void Execute(in SimulationTick tick) => server.ApplyInputs();
    }

    private sealed class IntentSystem(GameServer server) : ISimulationSystem
    {
        public SimulationStage Stage => SimulationStage.Transactions;

        public void Execute(in SimulationTick tick) => server.ApplyIntents(tick.Index);
    }
}
