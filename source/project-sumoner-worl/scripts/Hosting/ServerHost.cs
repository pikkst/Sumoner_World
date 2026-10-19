using Godot;
using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Core.Events;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Game.Net;
using SumonerWorld.Net.Hosting;
using SumonerWorld.Net.Sessions;

namespace SumonerWorld.Game.Hosting;

/// <summary>
/// The headless dedicated server (TDD §2.4, §3.1a): listens on UDP, runs the session handshake and the 30 Hz Core simulation from
/// a fixed-step accumulator. World systems are registered here as later M1 tasks land.
/// </summary>
public sealed partial class ServerHost : Node
{
    private const long NanosecondsPerSecond = 1_000_000_000L;

    private readonly HostOptions _options;
    private readonly ILog _log;
    private GodotENetTransport? _transport;
    private ServerEndpoint? _endpoint;
    private SimulationClock? _clock;
    private SimulationScheduler? _scheduler;
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
        _endpoint = new ServerEndpoint(_transport, new DevTokenAuthenticator(_options.DevSecret), _log, _options.MaxPlayers);
        _clock = new SimulationClock(SimulationSettings.Default);
        _scheduler = new SimulationScheduler(_clock, new EventBus());
        _accumulator = new FixedStepAccumulator(SimulationSettings.Default);
        _log.Write(LogLevel.Info, "server.listening", $"UDP port {_options.Port}, up to {_options.MaxPlayers} players, {SimulationSettings.DefaultTicksPerSecond} Hz");
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        _transport!.Poll(_endpoint!);
        int ticks = _accumulator!.Accumulate((long)(delta * NanosecondsPerSecond));
        for (int i = 0; i < ticks; i++)
        {
            _scheduler!.RunTick();
        }

        _endpoint!.ServerTick = _clock!.Current.Index;
        _transport.Flush();

        if (_options.QuitAfterTicks > 0 && _clock.Current.Index >= _options.QuitAfterTicks)
        {
            _log.Write(LogLevel.Info, "server.stopping", $"reached {_options.QuitAfterTicks} ticks");
            GetTree().Quit();
        }
    }

    /// <inheritdoc />
    public override void _ExitTree() => _transport?.Dispose();
}
