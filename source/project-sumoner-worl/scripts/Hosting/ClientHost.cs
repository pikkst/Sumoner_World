using Godot;
using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Core.Movement;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Game.Characters;
using SumonerWorld.Game.Combat;
using SumonerWorld.Game.Creatures;
using SumonerWorld.Game.Data;
using SumonerWorld.Game.DevTools;
using SumonerWorld.Game.Net;
using SumonerWorld.Game.Playtest;
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
/// interpolated, posed by their emotes) and a camera behind the own character that the right mouse button turns (<see cref="OrbitCamera"/>); <see cref="HostOptions.Screenshot"/> saves a frame
/// once creatures are on screen and quits. <see cref="InteractionControls"/> give the Memory Grove keys (M1-23); with
/// <see cref="HostOptions.SelfTest"/> a <see cref="PlaytestSelfTest"/> uses them first. In dev builds and with <see cref="HostOptions.DevTools"/> the <see cref="DevToolsLayer"/>
/// adds the dev overlay (F3; shown at start with <c>--dev-tools</c>) and the AI Inspector (F4, click, Tab;
/// <see cref="HostOptions.Inspect"/> opens it at start, and a screenshot then waits for the first inspection). A lost connection is retried every
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
    private const float BodyCentre = 0.9f;
    private const float TurnRate = 14f;
    private const float SpeedSmoothing = 8f;

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
    private HumanoidRig? _avatar;
    private InteractionControls? _interactions;
    private CompanionBar? _companionBar;
    private Vector3 _lastAvatarPosition;
    private float _avatarYaw;
    private float _avatarSpeed;
    private DevToolsLayer? _devTools;
    private PlaytestSelfTest? _selfTest;
    private OrbitCamera? _orbit;
    private CombatControls? _combat;
    private LockOn? _lockOn;
    private byte _ownHealth = byte.MaxValue;
    private Core.Data.DataRegistry? _data;
    private bool _inspectRequested;
    private double _devToolsStarted;

    /// <summary>Creates the host.</summary>
    public ClientHost(HostOptions options, ILog log)
    {
        _options = options;
        _log = log;
    }

    /// <summary>The camera the player sees through (null without a display).</summary>
    public Camera3D? Camera => _region?.Camera;

    /// <inheritdoc />
    public override void _Ready()
    {
        _transport = GodotENetTransport.Connect(_options.Address, _options.Port, out ConnectionId _);
        string ticket = DevTokenAuthenticator.Ticket(_options.Account, _options.DevSecret);
        _data = GameData.Load();
        Core.Data.Definitions.CombatDefinition combat = _data.Combat;
        _client = new GameClient(_transport, _log, new Hello(0, ticket), DevWorld.Movement(), new DodgePrediction(combat.DodgeTicks, combat.DodgeSpeedPercent));
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
            _client!.Tick(ReadMovement(), _region is null || (_interactions?.Menu.IsOpen ?? false) ? (byte)0 : CombatControls.Buttons);
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

    // WASD relative to the camera (W = away from it, D = to its right), turned into world-axis input for the server.
    private MoveInput ReadMovement()
    {
        int right = (Input.IsKeyPressed(Key.D) ? 1 : 0) - (Input.IsKeyPressed(Key.A) ? 1 : 0);
        int forward = (Input.IsKeyPressed(Key.W) ? 1 : 0) - (Input.IsKeyPressed(Key.S) ? 1 : 0);
        if (right == 0 && forward == 0)
        {
            return MoveInput.None;
        }

        Basis basis = _region?.Camera?.GlobalBasis ?? Basis.Identity;
        Vector3 ahead = new Vector3(-basis.Z.X, 0, -basis.Z.Z).Normalized();
        Vector3 side = new Vector3(basis.X.X, 0, basis.X.Z).Normalized();
        Vector3 direction = ((ahead * forward) + (side * right)).Normalized();
        return new MoveInput((short)Mathf.RoundToInt(direction.X * MoveInput.MaxAxis), (short)Mathf.RoundToInt(direction.Z * MoveInput.MaxAxis));
    }

    private void ShowWorld()
    {
        _region = new TestRegion(Core.World.DevRegion.Chunks, _log) { FreeFly = false };
        AddChild(_region);
        _orbit = new OrbitCamera();
        AddChild(_orbit);
        Core.Data.DataRegistry data = _data!;
        _views = new EntityViewSync(_client!, data, new QuadrupedEmotePresenter());
        AddChild(_views);
        var controls = _interactions = new InteractionControls(_client!, _views, data, AvatarCentre);
        AddChild(controls);
        _lockOn = new LockOn(_client!, _views, () => _region?.Camera, AvatarCentre);
        AddChild(_lockOn);
        _combat = new CombatControls(
            _client!, () => _region?.Camera, () => _lockOn.TryGetTarget(out Vector3 target) && _avatar is not null ? (AvatarCentre(), target) : null);
        AddChild(_combat);
        AddChild(new CombatHud(_client!));
        _companionBar = new CompanionBar(_client!, data, _views, controls);
        AddChild(_companionBar);
        if (_options.SelfTest)
        {
            _selfTest = new PlaytestSelfTest(controls, _companionBar, _combat, _lockOn, _client!, _orbit, () => _views.Shown > 0 && _elapsed > ScreenshotAfterSeconds, _log, Capture);
            AddChild(_selfTest);
        }

        if (_options.DevTools || OS.IsDebugBuild())
        {
            _devTools = new DevToolsLayer(_client!, _views, data, AvatarCentre);
            AddChild(_devTools);
        }

        _avatar = HumanoidRig.Build(EntityViewSync.OwnRobe);
        AddChild(_avatar);
    }

    // The self-test's extra frames: next to the --screenshot file, with the step's name (nothing without --screenshot).
    private void Capture(string name)
    {
        if (_options.Screenshot.Length > 0)
        {
            string path = System.IO.Path.ChangeExtension(_options.Screenshot, null) + $"-{name}.png";
            _log.Write(LogLevel.Info, "client.screenshot", $"{path}: {GetViewport().GetTexture().GetImage().SavePng(path)}");
        }
    }

    // Where the own character's body is (lock-on, aiming and interaction distances measure from here, not the feet).
    private Vector3 AvatarCentre() => _avatar is null ? Vector3.Zero : _avatar.GlobalPosition + new Vector3(0, BodyCentre, 0);

    // The own character faces where it runs, and the camera's way while it fights, blocks or is locked on (attacks go that way).
    private float AvatarYaw(Vector3 moved, float cameraYaw, double delta)
    {
        bool fighting = _client!.OwnVitals.Action != (byte)CombatAction.None || (_lockOn?.TryGetTarget(out _) ?? false);
        float wanted = !fighting && moved.LengthSquared() > 1e-6f ? Mathf.Atan2(moved.X, moved.Z) : fighting ? cameraYaw : _avatarYaw;
        _avatarYaw = Mathf.LerpAngle(_avatarYaw, wanted, 1 - Mathf.Exp(-TurnRate * (float)delta));
        return _avatarYaw;
    }

    private void FollowAvatar()
    {
        if (_region?.Camera is not Camera3D camera || _avatar is null || !_client!.Prediction.IsInitialized)
        {
            return;
        }

        System.Numerics.Vector3 p = _client.Prediction.Position;
        var feet = new Vector3(p.X, p.Y, p.Z);
        var position = feet + new Vector3(0, BodyCentre, 0);
        double delta = GetProcessDeltaTime();
        Vector3 moved = new Vector3(feet.X - _lastAvatarPosition.X, 0, feet.Z - _lastAvatarPosition.Z);
        // Prediction steps at 30 Hz while frames may come faster, so the measured speed is smoothed before it drives the gait.
        float measured = delta > 0 && moved.Length() < 2 ? moved.Length() / (float)delta : 0;
        _avatarSpeed = Mathf.Lerp(_avatarSpeed, measured, 1 - Mathf.Exp(-SpeedSmoothing * (float)delta));
        _lastAvatarPosition = feet;
        _avatar.Position = feet;
        Basis look = camera.GlobalBasis;
        CombatPose.Apply(_avatar, _client.OwnVitals.Action, AvatarYaw(moved, Mathf.Atan2(-look.Z.X, -look.Z.Z), delta), _elapsed);
        HumanoidAnimator.Animate(_avatar, (CombatAction)_client.OwnVitals.Action, _avatarSpeed, delta);
        if (_client.OwnVitals.Health < _ownHealth)
        {
            CombatPose.Hit(_avatar);
            HumanoidAnimator.Hit(_avatar);
        }

        _ownHealth = _client.OwnVitals.Health;
        CombatPose.Settle(_avatar, delta);
        _orbit!.Focus = _lockOn is not null && _lockOn.TryGetTarget(out Vector3 locked) ? locked : null;
        _orbit.Place(camera, position);
        StartDevTools();
        if (_options.Screenshot.Length > 0 && _views!.Shown > 0 && _elapsed > ScreenshotAfterSeconds && DevToolsReady() && (_selfTest?.Done ?? true))
        {
            Error error = GetViewport().GetTexture().GetImage().SavePng(_options.Screenshot);
            _log.Write(LogLevel.Info, "client.screenshot", $"{_options.Screenshot}: {error}, {_views.Shown} entities shown; overlay {_devTools?.OverlayVisible}, inspecting {_devTools?.Inspected}");
            GetTree().Quit(error == Error.Ok ? 0 : 1);
        }
    }

    // Once the first creatures are shown: the overlay with --dev-tools, the inspector with --inspect.
    private void StartDevTools()
    {
        if (_devTools is null || _inspectRequested || _views!.Shown == 0 || _elapsed < 2)
        {
            return;
        }

        _inspectRequested = true;
        _devToolsStarted = _elapsed;
        if (_options.DevTools)
        {
            _devTools.ShowOverlay(true);
        }

        if (_options.Inspect == "nearest")
        {
            _devTools.InspectNearest();
        }
        else if (_options.Inspect is "click" or "tab")
        {
            ulong target = _devTools.PressForTest(_options.Inspect);
            _log.Write(LogLevel.Info, "client.dev_tools", $"self-test '{_options.Inspect}': input events queued{(target != 0 ? $", clicking creature {target}" : string.Empty)}");
        }
        else if (ulong.TryParse(_options.Inspect, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out ulong id))
        {
            _devTools.Inspect(id);
        }

        _log.Write(LogLevel.Info, "client.dev_tools", $"overlay {_devTools.OverlayVisible}, inspecting {_devTools.Inspected}");
    }

    // A dev-tools screenshot waits for the queued input to be handled and the server's first answers.
    private bool DevToolsReady() =>
        _devTools is null
        || (_inspectRequested && _elapsed > _devToolsStarted + 3 && (_options.Inspect.Length == 0 || _devTools.Inspected != 0)
            && (!_devTools.OverlayVisible || _client!.DevMetrics is not null) && (_devTools.Inspected == 0 || _client!.Inspection is not null || _client.InspectionGone));

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
