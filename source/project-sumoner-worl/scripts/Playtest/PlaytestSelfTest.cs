using Godot;
using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Game.Combat;
using SumonerWorld.Game.Hosting;
using SumonerWorld.Net.Client;

namespace SumonerWorld.Game.Playtest;

/// <summary>
/// Plays the start of a Memory Grove session through the real controls (<c>--self-test</c>, M1-23, M2-02b): queues input events,
/// so input goes the same way as a tester's. Holds W until a creature is within 3 m (the snared calf 12 m ahead), then, one step
/// apart: a left click (a basic attack at the calf while it is still snared, so its health bar shows), T (lock on to it), a right-drag that the
/// lock ignores, T again (let go), a Pact while it is still snared (M2-05: P opens the menu, 1 and 2 choose two promises,
/// <paramref name="capture"/> saves frames of the menu and of the answer, Enter offers it), R (free it), E (feed it), Q (shout), a
/// right-button drag that turns the camera a quarter round, and Space held for a dodge. It logs the interaction lines, the attack's result and the calf's health, the own stamina, the calf's
/// emote while it answers and the answer line, then watches for <see cref="WatchSeconds"/> so the creature can react.
/// <see cref="Done"/> lets the host take its screenshot afterwards.
/// </summary>
public sealed partial class PlaytestSelfTest(
    InteractionControls controls, CombatControls combat, LockOn lockOn, GameClient client, OrbitCamera camera, Func<bool> worldShown, ILog log, Action<string> capture)
    : Node
{
    private const double WalkLimitSeconds = 8;
    private const float CloseMetres = 3f; // inside the basic attack's reach of a mossback (2 m + its 1.6 m body)
    private const double StepSeconds = 1.2;
    private const double WatchSeconds = 6;
    private const int LastStep = 12;

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
        if (_clock < (_step <= LastStep ? StepSeconds : WatchSeconds))
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
                PressKey(Key.T);
                break;
            case 2:
                log.Write(LogLevel.Info, "selftest.lock", $"lock-on allowed {client.LockOnAllowed}, target #{lockOn.Target}, camera at {Mathf.RadToDeg(camera.Yaw):F0}°");
                OrbitCamera.DragForTest(Mathf.Pi / 2 / OrbitCamera.Sensitivity); // ignored while locked
                break;
            case 3:
                log.Write(LogLevel.Info, "selftest.lock", $"after a drag while locked the camera is at {Mathf.RadToDeg(camera.Yaw):F0}° (target #{lockOn.Target})");
                PressKey(Key.T);
                PressKey(Key.P);
                break;
            case 4:
                log.Write(LogLevel.Info, "selftest.pact", $"menu open {controls.Menu.IsOpen} for {controls.Target.Name} #{controls.Target.Id} at {controls.TargetDistance:F1} m");
                PressKey(Key.Key1);
                PressKey(Key.Key2);
                break;
            case 5:
                log.Write(LogLevel.Info, "selftest.pact", $"chosen terms {controls.Menu.Chosen.Mask:b6}");
                capture("pact");
                PressKey(Key.Enter);
                break;
            case 6:
                log.Write(LogLevel.Info, "selftest.pact", $"menu open {controls.Menu.IsOpen}; the {controls.Target.Name} shows {TargetEmote()}; line so far: '{controls.Feedback}'");
                capture("answer");
                break;
            case 7:
                log.Write(LogLevel.Info, "selftest.pact", $"the {controls.Target.Name} shows {TargetEmote()}; answer: {controls.Feedback}");
                PressKey(Key.R);
                break;
            case 8:
                log.Write(LogLevel.Info, "selftest.feedback", controls.Feedback);
                PressKey(Key.E);
                break;
            case 9:
                log.Write(LogLevel.Info, "selftest.feedback", controls.Feedback);
                PressKey(Key.Q);
                break;
            case 10:
                log.Write(LogLevel.Info, "selftest.feedback", controls.Feedback);
                OrbitCamera.DragForTest(Mathf.Pi / 2 / OrbitCamera.Sensitivity);
                break;
            case 11:
                log.Write(LogLevel.Info, "selftest.camera", $"after the right-drag the camera is at {Mathf.RadToDeg(camera.Yaw):F0}° around the character");
                Press(Key.Space, pressed: true);
                break;
            case 12:
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

    private string TargetEmote() =>
        client.Entities.TryGetValue((SumonerWorld.Net.Messages.SnapshotEntityKind.Creature, controls.Target.Id), out SumonerWorld.Net.Messages.SnapshotEntity entity)
            ? ((SumonerWorld.Core.Cognition.Appraisal.Emote)entity.Emote).ToString()
            : "nothing (out of reach)";

    private int TargetHealth() =>
        client.Entities.TryGetValue((SumonerWorld.Net.Messages.SnapshotEntityKind.Creature, controls.Target.Id), out SumonerWorld.Net.Messages.SnapshotEntity entity) ? entity.Health : -1;

    private static void Press(Key key, bool pressed) => Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed });
}
