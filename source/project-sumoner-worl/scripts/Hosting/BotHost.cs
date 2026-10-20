using Godot;
using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Game.Net;
using SumonerWorld.Game.World;
using SumonerWorld.Net.Bots;
using SumonerWorld.Net.Hosting;
using SumonerWorld.Net.Transport;

namespace SumonerWorld.Game.Hosting;

/// <summary>
/// Runs a <see cref="BotFleet"/> over ENet at 30 Hz (B2, spike 2; TDD §8), one ENet host per bot. It stops when the server goes away
/// (the end of a B2 run) or after <see cref="HostOptions.QuitAfterTicks"/>. Exit code 0 when every bot was in a session up to the end
/// and the reconnect and duplicate checks passed, 3 otherwise.
/// </summary>
public sealed partial class BotHost : Node
{
    private const long NanosecondsPerSecond = 1_000_000_000L;
    private const int ExitPassed = 0;
    private const int ExitFailed = 3;
    private const int StatusEveryTicks = 900;
    private const int MaxFps = 120;

    private readonly HostOptions _options;
    private readonly ILog _log;
    private BotFleet? _fleet;
    private FixedStepAccumulator? _accumulator;
    private BotFleetReport? _lastFull;
    private bool _quitting;

    /// <summary>Creates the host.</summary>
    public BotHost(HostOptions options, ILog log)
    {
        _options = options;
        _log = log;
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        Engine.MaxFps = MaxFps;
        var settings = new BotFleetSettings
        {
            Count = _options.Bots,
            FirstAccount = _options.Account,
            DevSecret = _options.DevSecret,
            Movement = DevWorld.Movement(),
        };
        _fleet = new BotFleet(settings, () => GodotENetTransport.Connect(_options.Address, _options.Port, out ConnectionId _), _log);
        _accumulator = new FixedStepAccumulator(SimulationSettings.Default);
        _log.Write(LogLevel.Info, "bots.connecting", $"{_options.Bots} bots to {_options.Address}:{_options.Port}, accounts from {_options.Account}");
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_quitting)
        {
            return;
        }

        int ticks = _accumulator!.Accumulate((long)(delta * NanosecondsPerSecond));
        for (int i = 0; i < ticks; i++)
        {
            _fleet!.Step();
            BotFleetReport report = _fleet.Report();
            if (report.InSession == report.Bots)
            {
                _lastFull = report;
            }
            else if (_lastFull is not null && report.InSession == 0 && _fleet.Reconnect != BotCheck.Running)
            {
                Finish(_lastFull, "the server closed the sessions");
                return;
            }

            if (_fleet.Tick % StatusEveryTicks == 0)
            {
                _log.Write(LogLevel.Info, "bots.status", report.ToString());
            }

            if (_options.QuitAfterTicks > 0 && (ulong)_fleet.Tick >= _options.QuitAfterTicks)
            {
                Finish(report, $"reached {_options.QuitAfterTicks} ticks");
                return;
            }
        }

        foreach (Bot bot in _fleet!.Bots)
        {
            ((GodotENetTransport)bot.Transport).Flush();
        }
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        foreach (Bot bot in _fleet?.Bots ?? [])
        {
            ((GodotENetTransport)bot.Transport).Dispose();
        }
    }

    private void Finish(BotFleetReport report, string why)
    {
        _quitting = true;
        _log.Write(report.Passed ? LogLevel.Info : LogLevel.Error, "bots.finished", $"{why}: {report}");
        GetTree().Quit(report.Passed ? ExitPassed : ExitFailed);
    }
}
