using Godot;
using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.World;
using SumonerWorld.Game.Data;
using SumonerWorld.Game.Net;
using SumonerWorld.Net.Hosting;
using SumonerWorld.Net.Server;
using SumonerWorld.Net.Sessions;

namespace SumonerWorld.Game.Hosting;

/// <summary>
/// The headless dedicated server (TDD §2.4, §3.1a): listens on UDP and runs the authoritative <see cref="GameServer"/> loop at 30 Hz
/// from a fixed-step accumulator. Until the M2 ecosystem exists, the dev world is the 512 m test region with 200 seeded mossbacks.
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

    /// <summary>Creates the host.</summary>
    public ServerHost(HostOptions options, ILog log)
    {
        _options = options;
        _log = log;
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        _transport = GodotENetTransport.Listen(_options.Port, _options.MaxPlayers);
        _world = new CreatureWorld(GameData.Load(), new CreatureWorldSettings(), DevWorldSeed);
        _server = new GameServer(
            _world, _transport, new DevTokenAuthenticator(_options.DevSecret), _log, new GameServerSettings { MaxPlayers = _options.MaxPlayers });
        _server.Populate(DevSpecies, DevCreatures, DevHerds);
        _accumulator = new FixedStepAccumulator(SimulationSettings.Default);
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
        if (_options.QuitAfterTicks > 0 && tick >= _options.QuitAfterTicks)
        {
            _log.Write(LogLevel.Info, "server.stopping", $"reached {_options.QuitAfterTicks} ticks");
            GetTree().Quit();
        }
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        _transport?.Dispose();
        _world?.Dispose();
    }
}
