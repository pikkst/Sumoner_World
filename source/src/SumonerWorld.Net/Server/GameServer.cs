using System.Numerics;
using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Combat;
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

    /// <summary>Highest session id used before (after a restart: the highest in the saved dedupe table), so ids never repeat.</summary>
    public ulong LastSessionId { get; init; }

    /// <summary>
    /// Answer dev-tool requests (M1-22): tick metrics for the dev overlay and the AI Inspector. Dev servers only: it shows hidden
    /// creature minds to any connected client and costs server time.
    /// </summary>
    public bool DevTools { get; init; }

    /// <summary>Hardcore preset (GDD §9): no lock-on for clients. Normal is the default.</summary>
    public bool Hardcore { get; init; }

    /// <summary>Gathering nodes of each kind placed around the spawn point at start (0 = none; the game uses 4, M2-13).</summary>
    public int GatherNodesPerKind { get; init; }
}

/// <summary>
/// The authoritative server loop (TDD §3.2): poll the transport → run one simulation tick (player inputs at stage 1, the creature
/// systems, intents through the <see cref="ITransactionProcessor"/> at stage 9) → send snapshots. Clients send inputs and intents,
/// never state (R31). Sessions (TDD §3.5): an avatar appears when a session opens. When the connection is lost it stays in the world
/// for <see cref="LingerTicks"/> (no escape from danger by disconnecting), then is parked; resuming within the reconnect window puts
/// it back where it was, with the same intent dedupe window. When the session closes, the avatar leaves for good. With an
/// <see cref="ITransactionJournal"/>, an intent's result is sent only after the journal has made it durable (TDD §4.2).
/// With <see cref="GameServerSettings.DevTools"/>, a <see cref="DevToolsHost"/> times every tick and every
/// <see cref="DevReportEveryTicks"/> sends each client that asked for them the tick metrics and the inspected creature's mind.
/// </summary>
public sealed class GameServer : IServerSessionHandler
{
    /// <summary>30 s at 30 Hz: how long a disconnected player's avatar stays in the world.</summary>
    public const ulong LingerTicks = 900;

    /// <summary>Dev reports go out twice a second.</summary>
    public const int DevReportEveryTicks = 15;

    /// <summary>Ticks between checks whether a player's companion list changed (half a second).</summary>
    public const int CompanionCheckTicks = 15;

    private const ulong GatherNodeSeed = 0x6A7E_5EED;

    private readonly CreatureWorld _world;
    private readonly ITransport _transport;
    private readonly ILog _log;
    private readonly GameServerSettings _settings;
    private readonly ServerEndpoint _endpoint;
    private readonly SimulationScheduler _scheduler;
    private readonly IntentDeduplicator _dedupe = new();
    private readonly List<PlayerSlot> _players = [];
    private readonly Dictionary<ConnectionId, PlayerSlot> _byConnection = [];
    private readonly Dictionary<ulong, PlayerSlot> _bySession = [];
    private readonly Dictionary<ulong, PlayerSave> _knownPlayers = [];
    private readonly PlayerInput[] _batch = new PlayerInput[InputBatch.MaxInputs];
    private readonly byte[] _buffer = new byte[Protocol.MaxMessageSize];
    private readonly IntentRequestCodec _intentCodec = new();
    private readonly IntentAckCodec _ackCodec = new();
    private readonly CompanionListSender _companions;
    private readonly PlayerIntents _intents;
    private readonly SnapshotSender _snapshots;
    private readonly ITransactionJournal? _journal;
    private readonly Queue<(IntentId Id, IntentResult Result)> _pendingAcks = new();
    private readonly DevToolsHost? _dev;

    /// <summary>Creates the server around a world.</summary>
    public GameServer(
        CreatureWorld world,
        ITransport transport,
        IAuthenticator authenticator,
        ILog log,
        GameServerSettings settings,
        IMetrics? metrics = null,
        ITransactionJournal? journal = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(settings);

        _world = world;
        _transport = transport;
        _log = log;
        _settings = settings;
        _journal = journal;
        _endpoint = new ServerEndpoint(transport, authenticator, log, settings.MaxPlayers, this, settings.LastSessionId)
        {
            Rules = settings.Hardcore ? ServerRules.None : ServerRules.LockOn,
            DataFingerprint = world.Data.Fingerprint,
        };
        Movement = new MovementModel(settings.Movement);
        Transactions = new TransactionProcessor(_dedupe);
        _snapshots = new SnapshotSender(world, transport, settings.InterestRadius);
        _companions = new CompanionListSender(world, transport);
        _intents = new PlayerIntents(world, Transactions);
        if (settings.GatherNodesPerKind > 0)
        {
            // Placed from the same seed at every start: nodes are not saved, their places do not change.
            world.Economy.PlaceNodes(world.Data, world.Settings.Ground, settings.SpawnPoint, settings.GatherNodesPerKind, GatherNodeSeed);
        }
        _dev = settings.DevTools ? new DevToolsHost(world, transport, metrics) : null;
        _scheduler = new SimulationScheduler(world.Clock, world.Events, _dev?.Metrics ?? metrics);
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

    /// <summary>Intent results waiting for the journal.</summary>
    public int PendingAcks => _pendingAcks.Count;

    /// <summary>Snapshot parts sent since the start.</summary>
    public int SnapshotPartsSent => _snapshots.PartsSent;

    /// <summary>The dev tools, when <see cref="GameServerSettings.DevTools"/> is on.</summary>
    public DevToolsHost? DevTools => _dev;

    /// <summary>Adds a system to the tick after the standard ones in its stage (the playtest's telemetry, M1-23).</summary>
    public void AddSystem(ISimulationSystem system) => _scheduler.Register(system);

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

    /// <summary>Where every known player is or was last (for saving, M1-17b), by account.</summary>
    public IReadOnlyList<PlayerSave> CapturePlayers()
    {
        foreach (PlayerSlot player in _players)
        {
            if (player.InWorld)
            {
                _knownPlayers[player.Account] = SaveOf(player);
            }
        }

        return [.. _knownPlayers.OrderBy(entry => entry.Key).Select(entry => entry.Value)];
    }

    /// <summary>Loads saved players: each appears where it was when it next joins.</summary>
    public void RestorePlayers(IEnumerable<PlayerSave> players)
    {
        ArgumentNullException.ThrowIfNull(players);
        foreach (PlayerSave player in players)
        {
            PlayerRecords.Restore(_world, player);

            _knownPlayers[player.Account] = player;
        }
    }

    /// <summary>
    /// Adds <paramref name="count"/> wild creatures of a species at seeded random spots, in groups <paramref name="firstGroup"/> …
    /// <paramref name="firstGroup"/> + <paramref name="herds"/> − 1 (dev servers before the M2 ecosystem).
    /// </summary>
    public void Populate(string speciesId, int count, int herds, int firstGroup = 0)
    {
        ushort species = _world.SpeciesIndex(speciesId);
        Vector3 size = _world.BoundsMax - _world.BoundsMin;
        for (int i = 0; i < count; i++)
        {
            var position = _world.BoundsMin + new Vector3(
                (float)(_world.Random.NextDouble() * size.X), 0, (float)(_world.Random.NextDouble() * size.Z));
            _world.SpawnCreature(species, position, Personality.Roll(_world.Data.Species[species], _world.Random), firstGroup + _world.Random.NextInt(0, herds));
        }
    }

    /// <summary>One server tick: receive, simulate, replicate. The host flushes the transport afterwards.</summary>
    public void Tick()
    {
        _dev?.Metrics.BeginServerTick();
        SendDurableAcks();
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

        if (tick % CompanionCheckTicks == 0)
        {
            foreach (PlayerSlot player in _players)
            {
                if (player.Connection is ConnectionId connection && player.InWorld)
                {
                    _companions.Update(connection, player.Account);
                }
            }
        }

        if (tick % IntentDeduplicator.MaxIdleTicks == 0)
        {
            _dedupe.Prune(tick);
        }

        _dev?.Metrics.EndServerTick();
        if (tick % DevReportEveryTicks == 0)
        {
            _dev?.Report(tick, ConnectedCount);
        }
    }

    /// <inheritdoc />
    public void OnSessionOpened(ConnectionId connection, ulong accountId, ulong sessionId)
    {
        var player = new PlayerSlot(accountId, sessionId)
        {
            Connection = connection,
            LastPosition = _knownPlayers.TryGetValue(accountId, out PlayerSave known) ? known.Position : _settings.SpawnPoint,
        };
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
            _dev?.Forget(connection);
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
            _dev?.Forget(old);
        }

        player.Connection = connection;
        _byConnection.Add(connection, player);
        _companions.Forget(player.Account); // the new connection gets the list again
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
            _dev?.Forget(connection);
        }

        if (player.InWorld)
        {
            Leave(player);
        }

        _players.Remove(player);
        _intents.Forget(player.Account);
        _companions.Forget(player.Account);
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
            case MessageType.DevRequest when _dev is not null:
                _dev.OnRequest(connection, ref reader);
                break;
            case MessageType.DevRequest:
                _log.Write(LogLevel.Warning, "dev.refused", $"account {player.Account} asked for dev tools; this server runs without them");
                break;
            default:
                throw new MalformedMessageException($"{type} is not a client message.");
        }
    }

    private PlayerSave SaveOf(PlayerSlot player) => PlayerRecords.Capture(_world, player.Account, player.Avatar);

    private void Enter(PlayerSlot player)
    {
        player.Avatar = _world.SpawnPlayerAvatar(player.Account, player.LastPosition);
        player.InWorld = true;
        if (_knownPlayers.TryGetValue(player.Account, out PlayerSave saved))
        {
            PlayerRecords.Enter(_world, player.Avatar, saved);
        }
    }

    private void Leave(PlayerSlot player)
    {
        player.LastPosition = _world.World.GetComponent<Position>(player.Avatar).Value;
        _knownPlayers[player.Account] = SaveOf(player);
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

    private void ApplyInputs(ulong now)
    {
        foreach (PlayerSlot player in _players)
        {
            if (player.Connection is null || !player.InWorld)
            {
                continue;
            }

            int steps = player.Inputs.Buffered > PlayerInputBuffer.CatchUpThreshold ? 2 : 1;
            for (int i = 0; i < steps && player.Inputs.TryTakeNext(out PlayerInput input); i++)
            {
                ApplyInput(player, input, now);
            }
        }
    }

    // Buttons first (block held, dodge on press), then movement: none while down or staggered, a dash while dodging (combat spec §5).
    private void ApplyInput(PlayerSlot player, PlayerInput input, ulong now)
    {
        EcsWorld world = _world.World;
        var move = new MoveInput(input.MoveX, input.MoveZ);
        Vector3 direction = MovementModel.Direction(move);
        if (world.Has<SigilCast>(player.Avatar))
        {
            // Drawing a summoning sigil holds the player still and ignores guard and dodge until it is done (M2-06).
            player.LastButtons = input.Buttons;
            return;
        }

        CombatRules.SetBlock(world, player.Avatar, (input.Buttons & PlayerButtons.Block) != 0, now);
        bool dodgePressed = (input.Buttons & PlayerButtons.Dodge) != 0 && (player.LastButtons & PlayerButtons.Dodge) == 0;
        player.LastButtons = input.Buttons;
        if (dodgePressed)
        {
            CombatRules.TryDodge(world, _world.Data, player.Avatar, direction != Vector3.Zero ? direction : world.GetComponent<Facing>(player.Avatar).Value, now);
        }

        if (world.Has<Downed>(player.Avatar) || world.Has<Staggered>(player.Avatar))
        {
            return;
        }

        ref Position position = ref world.GetComponent<Position>(player.Avatar);
        if (CombatRules.IsEvading(world, _world.Data, player.Avatar, now))
        {
            Dodging dodge = world.GetComponent<Dodging>(player.Avatar);
            position = new Position(Movement.Dash(position.Value, new Vector3(dodge.DirX, 0, dodge.DirZ), _world.Data.Combat.DodgeSpeedPercent));
        }
        else
        {
            position = new Position(Movement.Advance(position.Value, move));
            if (direction != Vector3.Zero && !world.Has<ActiveMove>(player.Avatar))
            {
                world.GetComponent<Facing>(player.Avatar) = new Facing(Vector3.Normalize(direction));
            }
        }

        _world.Grid.Move(player.Avatar, position.Value);
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
                var id = new IntentId(player.Session, request.Sequence);
                int duplicatesBefore = Transactions.Duplicates;
                IntentResult result = _intents.Execute(id, player.Avatar, request, now, out Entity? changed);
                if (_journal is null)
                {
                    SendAck(connection, request.Sequence, result);
                }
                else
                {
                    bool isNew = Transactions.Duplicates == duplicatesBefore && result.Reason != IntentRejectReason.Stale;
                    IReadOnlyList<CreatureSave> changes = isNew && result.IsApplied && changed is Entity creature
                        ? [WorldSnapshots.CaptureCreature(_world, creature)]
                        : [];
                    _journal.Append(new JournalEntry(id, result, now, isNew, changes));
                    _pendingAcks.Enqueue((id, result));
                }

                if (!result.IsApplied && _log.IsEnabled(LogLevel.Debug))
                {
                    _log.Write(LogLevel.Debug, "intent.rejected", $"account {player.Account} intent {request.Sequence} {request.Kind}: {result.Reason}");
                }
            }
        }
    }

    private void SendDurableAcks()
    {
        while (_journal is not null && _journal.TryTakeDurable(out IntentId durable))
        {
            (IntentId id, IntentResult result) = _pendingAcks.Dequeue();
            if (id != durable)
            {
                throw new InvalidOperationException($"The journal returned {durable} but {id} was appended first.");
            }

            if (_bySession.TryGetValue(id.Session, out PlayerSlot? player) && player.Connection is ConnectionId connection)
            {
                SendAck(connection, id.Sequence, result);
            }
        }
    }

    private void SendAck(ConnectionId connection, uint sequence, IntentResult result)
    {
        var writer = new MessageWriter(_buffer);
        _ackCodec.Write(ref writer, new IntentAck(sequence, result));
        _transport.Send(connection, Channel.ReliableOrdered, writer.WrittenSpan);
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

        public byte LastButtons { get; set; }
    }

    private sealed class PlayerInputSystem(GameServer server) : ISimulationSystem
    {
        public SimulationStage Stage => SimulationStage.ApplyInputs;

        public void Execute(in SimulationTick tick) => server.ApplyInputs(tick.Index);
    }

    private sealed class IntentSystem(GameServer server) : ISimulationSystem
    {
        public SimulationStage Stage => SimulationStage.Transactions;

        public void Execute(in SimulationTick tick) => server.ApplyIntents(tick.Index);
    }
}
