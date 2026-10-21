using System.Runtime.InteropServices;
using Godot;
using SumonerWorld.Core.Data;
using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.Transactions;
using SumonerWorld.Core.World;
using SumonerWorld.Game.Data;
using SumonerWorld.Game.Net;
using SumonerWorld.Game.World;
using SumonerWorld.Net.Hosting;
using SumonerWorld.Net.Server;
using SumonerWorld.Net.Sessions;
using SumonerWorld.Net.Transport;
using SumonerWorld.Persistence;
using SumonerWorld.Persistence.Backups;

namespace SumonerWorld.Game.Hosting;

/// <summary>
/// The headless dedicated server (TDD §2.4, §3.1a, §4.2): listens on UDP and runs the authoritative <see cref="GameServer"/> loop at
/// 30 Hz from a fixed-step accumulator. The world lives in <see cref="HostOptions.WorldPath"/>: a saved world is loaded, otherwise a
/// new one is made (until the M2 ecosystem exists: <see cref="DevWorld.Populate"/>) and saved at once. Everything
/// walks on the <see cref="DevWorld"/> ground; creatures find paths on a navigation mesh baked from it at start-up, and Jolt gets
/// the same ground as height-map collision (checked against the heightfield once at start). A checkpoint
/// goes to the persistence thread every 30 s, and every intent's result is sent only after the thread made it durable; on shutdown a last checkpoint is written and a backup made.
/// With <see cref="HostOptions.BenchmarkOut"/> it runs B2 (M0-07): a <see cref="ServerBenchmark"/> measures once the bots are in and
/// warmed up, writes <c>summary.json</c> and <c>ticks.csv</c> there and stops the server. With <see cref="HostOptions.DevTools"/>
/// (the Docker dev server sets <c>SW_DEV_TOOLS</c>) it answers the dev overlay and the AI Inspector (M1-22). In the playtest
/// (<see cref="HostMode.Playtest"/>) it listens on localhost only, starts a fresh world every session in <c>user://playtest/</c> and
/// spawns the Memory Grove around the player's start instead of the dev population.
/// </summary>
public sealed partial class ServerHost : Node
{
    private const long NanosecondsPerSecond = 1_000_000_000L;
    private const ulong DevWorldSeed = 0x53570001;
    private const int MaxFps = 120;
    private const string WireStatistics = "/sys/class/net/eth0/statistics/";

    private readonly HostOptions _options;
    private readonly ILog _log;
    private GodotENetTransport? _transport;
    private CreatureWorld? _world;
    private GameServer? _server;
    private ServerBenchmark? _benchmark;
    private FixedStepAccumulator? _accumulator;
    private WorldStore? _store;
    private PersistenceWriter? _writer;
    private ulong _lastCheckpoint;
    private GodotNavigation? _navigation;
    private GodotPhysicsQueries? _physics;
    private bool _physicsChecked;
    private PosixSignalRegistration? _sigterm;
    private PosixSignalRegistration? _sigint;
    private volatile bool _stopRequested;

    /// <summary>Creates the host.</summary>
    public ServerHost(HostOptions options, ILog log)
    {
        _options = options;
        _log = log;
    }

    /// <summary>The running server (after <see cref="_Ready"/>).</summary>
    public GameServer? Server => _server;

    /// <inheritdoc />
    public override void _Ready()
    {
        string path = WorldPath();
        if (Playtest)
        {
            // A fresh grove for every tester: never continue the last session's world.
            string folder = Path.GetDirectoryName(path)!;
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }

            Directory.CreateDirectory(folder);
        }

        SavedWorld? saved = OpenWorld(path);

        // Headless Godot would otherwise spin a core between ticks; 120 frames/s keeps the 30 Hz accumulator smooth.
        Engine.MaxFps = MaxFps;
        // Twice the player limit in ENet peers: a client that lost its connection reconnects before ENet times the old peer out
        // (up to 30 s), so everyone can resume at once. The session endpoint enforces the player limit itself.
        _transport = GodotENetTransport.Listen(_options.Port, _options.MaxPlayers * 2, Playtest ? "127.0.0.1" : "*");
        var metered = new MeteredTransport(_transport);
        _benchmark = CreateBenchmark(metered);
        _writer = new PersistenceWriter(_store, _log);
        var settings = new GameServerSettings
        {
            MaxPlayers = _options.MaxPlayers,
            LastSessionId = _store.MaxIntentSession(),
            Movement = DevWorld.Movement(),
            DevTools = _options.DevTools,
            Hardcore = _options.Hardcore,
        };
        _server = new GameServer(_world, metered, new DevTokenAuthenticator(_options.DevSecret), _log, settings, _benchmark, _writer);
        if (saved is null)
        {
            string population = DevWorld.Populate(_server, Playtest);
            Checkpoint();
            _log.Write(LogLevel.Info, "world.created", $"{path}: new world, {population}");
        }
        else
        {
            _server.RestorePlayers(saved.Players);
            _lastCheckpoint = _world.Clock.Current.Index;
            _log.Write(LogLevel.Info, "world.loaded", $"{path}: tick {saved.World.Tick}, {saved.World.Creatures.Count} creatures, {saved.Players.Count} players");
        }

        _accumulator = new FixedStepAccumulator(SimulationSettings.Default);

        // docker stop sends SIGTERM: quit through the main loop so the last checkpoint and the shutdown backup are written.
        _sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, RequestStop);
        _sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, RequestStop);
        _log.Write(LogLevel.Info, "server.listening", $"UDP port {_options.Port}, up to {_options.MaxPlayers} players, {SimulationSettings.DefaultTicksPerSecond} Hz, {_world.World.Store<MindRef>().Count} creatures, dev tools {(_options.DevTools ? "on" : "off")}");
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        int ticks = _accumulator!.Accumulate((long)(delta * NanosecondsPerSecond));
        for (int i = 0; i < ticks; i++)
        {
            _benchmark?.BeginTick();
            _server!.Tick();
            _benchmark?.EndTick(_server.ConnectedCount);
        }

        if (_benchmark?.Phase == ServerBenchmarkPhase.Done)
        {
            FinishBenchmark();
            return;
        }

        _transport!.Flush();
        CheckPhysicsOnce();

        ulong tick = _world!.Clock.Current.Index;
        if (_stopRequested)
        {
            _log.Write(LogLevel.Info, "server.stopping", "stop signal received");
            _stopRequested = false;
            GetTree().Quit();
        }

        if (tick - _lastCheckpoint >= PersistenceWriter.DefaultIntervalTicks)
        {
            Checkpoint();
        }

        if (_options.QuitAfterTicks > 0 && tick >= _options.QuitAfterTicks)
        {
            _log.Write(LogLevel.Info, "server.stopping", $"reached {_options.QuitAfterTicks} ticks");
            GetTree().Quit();
        }
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        _sigterm?.Dispose();
        _sigint?.Dispose();
        _transport?.DisconnectAll();
        if (_writer is not null)
        {
            Checkpoint();
            if (!_writer.Flush(TimeSpan.FromSeconds(10)))
            {
                _log.Write(LogLevel.Error, "world.shutdown", "the last checkpoint did not finish in 10 s");
            }

            _writer.Dispose();
            _store!.Backup("shutdown");
            _log.Write(LogLevel.Info, "world.shutdown", $"saved at tick {_writer.LastSavedTick} and backed up; paths delivered {_world!.Paths.Delivered}, not found {_world.Paths.NotFound}, refused {_world.Paths.Refused}");
        }

        _store?.Dispose();
        _transport?.Dispose();
        _physics?.Dispose();
        _navigation?.Dispose();
        _world?.Dispose();
    }

    private void RequestStop(PosixSignalContext context)
    {
        context.Cancel = true;
        _stopRequested = true;
    }

    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(_store), nameof(_world))]
    private SavedWorld? OpenWorld(string path)
    {
        _store = WorldStore.Open(path, new WorldBackups(Path.Combine(Path.GetDirectoryName(path)!, "backups")));
        SavedWorld? saved = _store.LoadWorld();
        DataRegistry data = GameData.Load();
        CreatureWorldSettings settings = DevWorld.Creatures(OpenGround());
        _world = saved is null
            ? new CreatureWorld(data, settings, DevWorldSeed)
            : WorldSnapshots.Restore(data, settings, saved.World);
        return saved;
    }

    private GodotNavigation OpenGround()
    {
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        var root = new Node3D { Name = "Ground" };
        AddChild(root);
        GodotPhysicsQueries.AddTerrainCollision(root, DevWorld.Ground);
        _physics = new GodotPhysicsQueries(root);
        _navigation = GodotNavigation.Bake(DevWorld.Ground);
        _log.Write(LogLevel.Info, "world.ground", $"{DevWorld.Ground.Size} m terrain: collision added, navigation mesh baked ({_navigation.Polygons} polygons) in {System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalSeconds:F1} s");
        return _navigation;
    }

    private void CheckPhysicsOnce()
    {
        // Jolt sees new bodies after a physics step: compare its ground with the heightfield a second after start.
        if (_physicsChecked || _physics is null || _world!.Clock.Current.Index < SimulationSettings.DefaultTicksPerSecond)
        {
            return;
        }

        _physicsChecked = true;
        (int misses, float worst) = _physics.CompareWith(DevWorld.Ground);
        bool ok = misses == 0 && worst < 0.05f && _navigation!.IsReady;
        _log.Write(ok ? LogLevel.Info : LogLevel.Error, "world.physics_check", $"64 rays: {misses} missed, largest difference to the heightfield {worst * 100:F1} cm; navigation ready {_navigation!.IsReady}");
    }

    private ServerBenchmark? CreateBenchmark(MeteredTransport metered)
    {
        if (_options.BenchmarkOut.Length == 0)
        {
            return null;
        }

        var settings = new ServerBenchmarkSettings
        {
            Players = _options.BenchmarkPlayers,
            WarmupTicks = _options.WarmupTicks,
            MeasureTicks = _options.MeasureTicks,
        };
        _log.Write(LogLevel.Info, "b2.waiting", $"benchmark: waiting for {settings.Players} players, then {settings.WarmupTicks} warm-up and {settings.MeasureTicks} measured ticks");
        return new ServerBenchmark(settings, metered) { WireCounters = ReadWireCounters };
    }

    private static (long Received, long Sent)? ReadWireCounters()
    {
        try
        {
            return (long.Parse(File.ReadAllText(WireStatistics + "rx_bytes"), System.Globalization.CultureInfo.InvariantCulture),
                long.Parse(File.ReadAllText(WireStatistics + "tx_bytes"), System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void FinishBenchmark()
    {
        TransactionProcessor transactions = _server!.Transactions;
        ServerBenchmarkResult result = _benchmark!.Result(new TransactionCounts(transactions.Applied, transactions.Rejected, transactions.Duplicates));
        _benchmark.Write(_options.BenchmarkOut, result);
        string verdict = result.Passed ? "passed" : $"failed: {string.Join("; ", result.NetworkFailures.Concat(result.TimingFailures))}";
        _log.Write(result.Passed ? LogLevel.Info : LogLevel.Warning, "b2.finished", $"{result.MeasuredTicks} ticks, tick p99 {result.TickP99Ms:F2} ms, down {result.Bandwidth!.DownMean:F1} KiB/s per client; {verdict}");
        _benchmark = null;
        GetTree().Quit();
    }

    private void Checkpoint()
    {
        _writer!.Submit(WorldSnapshots.Capture(_world!), _server!.CapturePlayers());
        _lastCheckpoint = _world!.Clock.Current.Index;
    }

    private bool Playtest => _options.Mode == HostMode.Playtest;

    private string WorldPath()
    {
        string path = Playtest ? ProjectSettings.GlobalizePath("user://playtest/world.db")
            : _options.WorldPath.Length > 0 ? _options.WorldPath : ProjectSettings.GlobalizePath("user://world/world.db");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        return Path.GetFullPath(path);
    }
}
