using Godot;
using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Game.Combat;
using SumonerWorld.Game.Hosting;
using SumonerWorld.Net.Client;

namespace SumonerWorld.Game.Playtest;

/// <summary>
/// Plays the start of a Memory Grove session through the real controls (<c>--self-test</c>, M1-23, M2-02b): queues input events,
/// so input goes the same way as a tester's. Holds W until a creature is within 3 m (the snared calf 12 m ahead), then, one step
/// apart: a left click (a basic attack at the calf while it is still snared, so its health bar shows), R (free it), E (feed it),
/// Q (shout), a right-button drag that turns the camera a quarter round, and Space held for a dodge. It logs the interaction line, the attack's
/// result and the calf's health, and the own stamina, then watches for <see cref="WatchSeconds"/> so the creature can react. <see cref="Done"/> lets the host take
/// its screenshot afterwards.
/// </summary>
public sealed partial class PlaytestSelfTest(
    InteractionControls controls, CombatControls combat, GameClient client, OrbitCamera camera, Func<bool> worldShown, ILog log) : Node
{
    private const double WalkLimitSeconds = 8;
    private const float CloseMetres = 3f; // inside the basic attack's reach of a mossback (2 m + its 1.6 m body)
    private const double StepSeconds = 1.2;
    private const double WatchSeconds = 6;
    private static readonly Key[] Interactions = [Key.R, Key.E, Key.Q];

    private double _clock;
    private double _walking;
    private int _step = -1;

    /// <summary>All steps were done and the last answer had time to arrive.</summary>
    public bool Done { get; private set; }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (Done || !worldShown())
        {
            return;
        }

        if (_step < 0)
        {
            Walk(delta);
            return;
        }

        _clock += delta;
        if (_clock < (_step <= 6 ? StepSeconds : WatchSeconds))
        {
            return;
        }

        _clock = 0;
        Step();
        _step++;
    }

    private void Walk(double delta)
    {
        _walking += delta;
        bool inReach = controls.Target.Id != 0 && controls.TargetDistance < CloseMetres;
        Press(Key.W, pressed: !inReach && _walking < WalkLimitSeconds);
        if (inReach || _walking >= WalkLimitSeconds)
        {
            log.Write(LogLevel.Info, "selftest.reach", inReach ? $"{controls.Target.Name} #{controls.Target.Id} in reach after {_walking:F1} s" : "nothing in reach");
            _step = 0;
            _clock = 0;
        }
    }

    private void Step()
    {
        switch (_step)
        {
            case 0:
                log.Write(LogLevel.Info, "selftest.aim", $"attacking {controls.Target.Name} at {controls.TargetDistance:F1} m");
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
                break;
            case 1:
                log.Write(LogLevel.Info, "selftest.attack", $"attack: {combat.LastResult?.ToString() ?? "no answer"}; {controls.Target.Name} health {TargetHealth()}/255");
                PressKey(Interactions[0]);
                break;
            case <= 3:
                log.Write(LogLevel.Info, "selftest.feedback", controls.Feedback);
                PressKey(Interactions[_step - 1]);
                break;
            case 4:
                log.Write(LogLevel.Info, "selftest.feedback", controls.Feedback);
                OrbitCamera.DragForTest(Mathf.Pi / 2 / OrbitCamera.Sensitivity);
                break;
            case 5:
                log.Write(LogLevel.Info, "selftest.camera", $"after the right-drag the camera is at {Mathf.RadToDeg(camera.Yaw):F0}° around the character");
                Press(Key.Space, pressed: true);
                break;
            case 6:
                Press(Key.Space, pressed: false);
                log.Write(LogLevel.Info, "selftest.dodge", $"after the dodge: stamina {client.OwnVitals.Stamina}/255, health {client.OwnVitals.Health}/255");
                break;
            default:
                Done = true;
                break;
        }
    }

    private static void PressKey(Key key)
    {
        Press(key, pressed: true);
        Press(key, pressed: false);
    }

    private int TargetHealth() =>
        client.Entities.TryGetValue((SumonerWorld.Net.Messages.SnapshotEntityKind.Creature, controls.Target.Id), out SumonerWorld.Net.Messages.SnapshotEntity entity) ? entity.Health : -1;

    private static void Press(Key key, bool pressed) => Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed });
}
