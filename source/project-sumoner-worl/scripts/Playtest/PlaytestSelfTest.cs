using Godot;
using SumonerWorld.Core.Diagnostics;

namespace SumonerWorld.Game.Playtest;

/// <summary>
/// Plays the start of a Memory Grove session through the real controls (<c>--self-test</c>, M1-23): queues key events, so input
/// goes the same way as a tester's. Holds W until a creature is in reach (the snared calf 12 m ahead), then R (free it),
/// E (feed it) and Q (shout), one second apart, logs what the interaction line says, and watches for <see cref="WatchSeconds"/> so
/// the creature can react. <see cref="Done"/> lets the host take
/// its screenshot afterwards.
/// </summary>
public sealed partial class PlaytestSelfTest(InteractionControls controls, Func<bool> worldShown, ILog log) : Node
{
    private const double WalkLimitSeconds = 8;
    private const double StepSeconds = 1.2;
    private const double WatchSeconds = 6;
    private static readonly Key[] Steps = [Key.R, Key.E, Key.Q];

    private double _clock;
    private double _walking;
    private int _step = -1;

    /// <summary>All steps were pressed and the last answer had time to arrive.</summary>
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
            _walking += delta;
            bool inReach = controls.Target.Id != 0;
            Press(Key.W, pressed: !inReach && _walking < WalkLimitSeconds);
            if (inReach || _walking >= WalkLimitSeconds)
            {
                log.Write(LogLevel.Info, "selftest.reach", inReach ? $"{controls.Target.Name} #{controls.Target.Id} in reach after {_walking:F1} s" : "nothing in reach");
                _step = 0;
                _clock = 0;
            }

            return;
        }

        _clock += delta;
        if (_clock < (_step < Steps.Length ? StepSeconds : WatchSeconds))
        {
            return;
        }

        _clock = 0;
        log.Write(LogLevel.Info, "selftest.feedback", controls.Feedback);
        if (_step < Steps.Length)
        {
            Press(Steps[_step], pressed: true);
            Press(Steps[_step], pressed: false);
            _step++;
        }
        else
        {
            Done = true;
        }
    }

    private static void Press(Key key, bool pressed) => Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed });
}
