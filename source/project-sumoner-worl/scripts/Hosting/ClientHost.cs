using Godot;
using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Core.Movement;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Game.Creatures;
using SumonerWorld.Game.Data;
using SumonerWorld.Game.Net;
using SumonerWorld.Game.World;
using SumonerWorld.Net.Client;
using SumonerWorld.Net.Hosting;
using SumonerWorld.Net.Messages;
using SumonerWorld.Net.Sessions;
using SumonerWorld.Net.Transport;

namespace SumonerWorld.Game.Hosting;

/// <summary>
/// Connects to a server with a dev ticket and runs the <see cref="GameClient"/> at 30 Hz: WASD moves the own character with
/// prediction. With a display it shows the dev region, every creature and player the server sends (<see cref="EntityViewSync"/>,
/// interpolated, posed by their emotes) and a camera behind the own character; <see cref="HostOptions.Screenshot"/> saves a frame
/// once creatures are on screen and quits. A lost connection is retried every
/// <see cref="ReconnectEverySeconds"/> for <see cref="ReconnectForSeconds"/> with the session token, so the session resumes (TDD §3.5).
/// With <see cref="HostOptions.ExitOnWelcome"/> it is a smoke test: exit code 0 once welcomed and the first snapshot arrived, 2 when
/// rejected, 1 after <see cref="TimeoutSeconds"/>.
/// </summary>
public sealed partial class ClientHost : Node
{
    /// <summary>Seconds to wait for the server before giving up (smoke tests).</summary>
    public const double TimeoutSeconds = 20;

    /// <summary>Seconds between reconnect attempts.</summary>
    public const double ReconnectEverySeconds = 2;

    /// <summary>How long to keep trying (the server's reconnect window).</summary>
    public const double ReconnectForSeconds = 120;

    private const long NanosecondsPerSecond = 1_000_000_000L;
    private const int ExitWelcomed = 0;
    private const int ExitTimeout = 1;
    private const int ExitRejected = 2;
    private const int StatusEveryTicks = 150;
    private const double ScreenshotAfterSeconds = 8;

    private readonly HostOptions _options;
    private readonly ILog _log;
    private GodotENetTransport? _transport;
    private GameClient? _client;
    private FixedStepAccumulator? _accumulator;
    private double _elapsed;
    private double _disconnectedFor;
    private double _nextAttempt;
    private long _ticks;
    private TestRegion? _region;
    private EntityViewSync? _views;
    private Node3D? _avatar;

    /// <summary>Creates the host.</summary>
    public ClientHost(HostOptions options, ILog log)
    {
        _options = options;
        _log = log;
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        _transport = GodotENetTransport.Connect(_options.Address, _options.Port, out ConnectionId _);
        string ticket = DevTokenAuthenticator.Ticket(_options.Account, _options.DevSecret);
        _client = new GameClient(_transport, _log, new Hello(0, ticket), DevWorld.Movement());
        _accumulator = new FixedStepAccumulator(SimulationSettings.Default);
        _log.Write(LogLevel.Info, "client.connecting", $"{_options.Address}:{_options.Port} as account {_options.Account}");
        if (!_options.ExitOnWelcome && DisplayServer.GetName() != "headless")
        {
            ShowWorld();
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        int ticks = _accumulator!.Accumulate((long)(delta * NanosecondsPerSecond));
        for (int i = 0; i < ticks; i++)
        {
            _client!.Tick(ReadMovement());
            if (++_ticks % StatusEveryTicks == 0 && _client.Prediction.IsInitialized)
            {
                _log.Write(LogLevel.Info, "client.status", $"at {_client.Prediction.Position}, {_client.Entities.Count} entities in view, server tick {_client.LastSnapshotTick}");
            }
        }

        _transport!.Flush();
        _elapsed += delta;
        FollowAvatar();
        if (_options.ExitOnWelcome)
        {
            CheckSmokeTest();
        }
        else
        {
            KeepConnected(delta);
        }
    }

    /// <inheritdoc />
    public override void _ExitTree() => _transport?.Dispose();

    private static MoveInput ReadMovement()
    {
        int x = (Input.IsKeyPressed(Key.D) ? MoveInput.MaxAxis : 0) - (Input.IsKeyPressed(Key.A) ? MoveInput.MaxAxis : 0);
        int z = (Input.IsKeyPressed(Key.S) ? MoveInput.MaxAxis : 0) - (Input.IsKeyPressed(Key.W) ? MoveInput.MaxAxis : 0);
        return new MoveInput((short)x, (short)z);
    }

    private void ShowWorld()
    {
        _region = new TestRegion(Core.World.DevRegion.Chunks, _log) { FreeFly = false };
        AddChild(_region);
        _views = new EntityViewSync(_client!, GameData.Load(), new QuadrupedEmotePresenter());
        AddChild(_views);
        _avatar = new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = 0.35f, Height = 1.8f },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.75f, 0.6f, 0.25f) },
        };
        AddChild(_avatar);
    }

    private void FollowAvatar()
    {
        if (_region?.Camera is not Camera3D camera || _avatar is null || !_client!.Prediction.IsInitialized)
        {
            return;
        }

        System.Numerics.Vector3 p = _client.Prediction.Position;
        var position = new Vector3(p.X, p.Y + 0.9f, p.Z);
        _avatar.Position = position;
        camera.Position = position + new Vector3(0, 5, -10);
        camera.LookAt(position + new Vector3(0, 1, 0), Vector3.Up);
        if (_options.Screenshot.Length > 0 && _views!.Shown > 0 && _elapsed > ScreenshotAfterSeconds)
        {
            Error error = GetViewport().GetTexture().GetImage().SavePng(_options.Screenshot);
            _log.Write(LogLevel.Info, "client.screenshot", $"{_options.Screenshot}: {error}, {_views.Shown} entities shown");
            GetTree().Quit(error == Error.Ok ? 0 : 1);
        }
    }

    private void KeepConnected(double delta)
    {
        if (_client!.Session.State != ClientSessionState.Disconnected || _client.Session.Session is null)
        {
            _disconnectedFor = 0;
            _nextAttempt = 0;
            return;
        }

        _disconnectedFor += delta;
        if (_disconnectedFor > ReconnectForSeconds || _disconnectedFor < _nextAttempt)
        {
            return;
        }

        _nextAttempt = _disconnectedFor + ReconnectEverySeconds;
        _transport!.Dispose();
        _transport = GodotENetTransport.Connect(_options.Address, _options.Port, out ConnectionId _);
        _client.Reconnect(_transport);
        _log.Write(LogLevel.Info, "client.reconnecting", $"{_options.Address}:{_options.Port} after {_disconnectedFor:F0} s");
    }

    private void CheckSmokeTest()
    {
        if (_client!.Session.State == ClientSessionState.Rejected)
        {
            GetTree().Quit(ExitRejected);
        }
        else if (_client.LastSnapshotTick > 0)
        {
            _log.Write(LogLevel.Info, "client.smoke", $"welcomed; first snapshot at tick {_client.LastSnapshotTick} with {_client.Entities.Count} entities");
            GetTree().Quit(ExitWelcomed);
        }
        else if (_elapsed > TimeoutSeconds)
        {
            _log.Write(LogLevel.Error, "client.timeout", $"no session and snapshot from {_options.Address}:{_options.Port} within {TimeoutSeconds} s");
            GetTree().Quit(ExitTimeout);
        }
    }
}
