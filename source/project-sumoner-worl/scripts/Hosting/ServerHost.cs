using System.Runtime.InteropServices;
using Godot;
using SumonerWorld.Core.Data;
using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.World;
using SumonerWorld.Game.Data;
using SumonerWorld.Game.Net;
using SumonerWorld.Net.Hosting;
using SumonerWorld.Net.Server;
using SumonerWorld.Net.Sessions;
using SumonerWorld.Persistence;
using SumonerWorld.Persistence.Backups;

namespace SumonerWorld.Game.Hosting;

/// <summary>
/// The headless dedicated server (TDD §2.4, §3.1a, §4.2): listens on UDP and runs the authoritative <see cref="GameServer"/> loop at
/// 30 Hz from a fixed-step accumulator. The world lives in <see cref="HostOptions.WorldPath"/>: a saved world is loaded, otherwise a
/// new one is made (until the M2 ecosystem exists: the 512 m test region with 200 seeded mossbacks) and saved at once. A checkpoint
/// goes to the persistence thread every 30 s, and every intent's result is sent only after the thread made it durable; on shutdown a last checkpoint is written and a backup made.
/// </summary>
public sealed partial class ServerHost : Node
{
    private const long NanosecondsPerSecond = 1_000_000_000L;
    private const ulong DevWorldSeed = 0x53570001;
    private const string DevSpecies = "mossback";
    private const int DevCreatures = 200;
    private const int DevHerds = 10;

    private readonly HostOptions _options;
    private readonly ILog _log;
    private GodotENetTransport? _transport;
    private CreatureWorld? _world;
    private GameServer? _server;
    private FixedStepAccumulator? _accumulator;
    private WorldStore? _store;
    private PersistenceWriter? _writer;
    private ulong _lastCheckpoint;
    private PosixSignalRegistration? _sigterm;
    private PosixSignalRegistration? _sigint;
    private volatile bool _stopRequested;

    /// <summary>Creates the host.</summary>
    public ServerHost(HostOptions options, ILog log)
    {
        _options = options;
        _log = log;
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        string path = WorldPath();
        _store = WorldStore.Open(path, new WorldBackups(Path.Combine(Path.GetDirectoryName(path)!, "backups")));
        SavedWorld? saved = _store.LoadWorld();
        DataRegistry data = GameData.Load();
        _world = saved is null
            ? new CreatureWorld(data, new CreatureWorldSettings(), DevWorldSeed)
            : WorldSnapshots.Restore(data, new CreatureWorldSettings(), saved.World);
        _transport = GodotENetTransport.Listen(_options.Port, _options.MaxPlayers);
        _writer = new PersistenceWriter(_store, _log);
        var settings = new GameServerSettings { MaxPlayers = _options.MaxPlayers, LastSessionId = _store.MaxIntentSession() };
        _server = new GameServer(_world, _transport, new DevTokenAuthenticator(_options.DevSecret), _log, settings, journal: _writer);
        if (saved is null)
        {
            _server.Populate(DevSpecies, DevCreatures, DevHerds);
            Checkpoint();
            _log.Write(LogLevel.Info, "world.created", $"{path}: new world with {DevCreatures} creatures");
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
        _log.Write(LogLevel.Info, "server.listening", $"UDP port {_options.Port}, up to {_options.MaxPlayers} players, {SimulationSettings.DefaultTicksPerSecond} Hz, {DevCreatures} creatures");
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        int ticks = _accumulator!.Accumulate((long)(delta * NanosecondsPerSecond));
        for (int i = 0; i < ticks; i++)
        {
            _server!.Tick();
        }

        _transport!.Flush();

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
        if (_writer is not null)
        {
            Checkpoint();
            if (!_writer.Flush(TimeSpan.FromSeconds(10)))
            {
                _log.Write(LogLevel.Error, "world.shutdown", "the last checkpoint did not finish in 10 s");
            }

            _writer.Dispose();
            _store!.Backup("shutdown");
            _log.Write(LogLevel.Info, "world.shutdown", $"saved at tick {_writer.LastSavedTick} and backed up");
        }

        _store?.Dispose();
        _transport?.Dispose();
        _world?.Dispose();
    }

    private void RequestStop(PosixSignalContext context)
    {
        context.Cancel = true;
        _stopRequested = true;
    }

    private void Checkpoint()
    {
        _writer!.Submit(WorldSnapshots.Capture(_world!), _server!.CapturePlayers());
        _lastCheckpoint = _world!.Clock.Current.Index;
    }

    private string WorldPath()
    {
        string path = _options.WorldPath.Length > 0 ? _options.WorldPath : ProjectSettings.GlobalizePath("user://world/world.db");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        return Path.GetFullPath(path);
    }
}
