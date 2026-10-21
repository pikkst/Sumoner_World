using Godot;

namespace SumonerWorld.Game.Hosting;

/// <summary>
/// The third-person camera around the own character: hold the right mouse button and move the mouse to turn it around the
/// character (left/right) and raise or lower it (up/down); the mouse wheel moves it closer or further. The start view is behind
/// the character, 5 m up and 10 m back, looking along +Z. WASD stays relative to wherever the camera looks.
/// </summary>
public sealed partial class OrbitCamera : Node
{
    /// <summary>Radians turned per pixel of mouse movement.</summary>
    public const float Sensitivity = 0.005f;

    private const float MinPitch = 0.1f;
    private const float MaxPitch = 1.3f;
    private const float MinDistance = 3f;
    private const float MaxDistance = 25f;
    private const float ZoomStep = 1.1f;
    private const float LookHeight = 1f;

    private const float FocusFollow = 0.15f;

    private bool _turning;

    /// <summary>Angle around the character in radians; 0 is behind it on −Z, positive turns the view to the right.</summary>
    public float Yaw { get; private set; }

    /// <summary>Angle above the horizontal, in radians.</summary>
    public float Pitch { get; private set; } = Mathf.Atan2(5, 10);

    /// <summary>A locked-on target (combat lock-on): the camera swings behind the character to keep it in view; null = free.</summary>
    public Vector3? Focus { get; set; }

    /// <summary>Distance to the character in metres.</summary>
    public float Distance { get; private set; } = Mathf.Sqrt(125);

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Right } button:
                _turning = button.Pressed;
                Input.MouseMode = _turning ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                Distance = Mathf.Max(MinDistance, Distance / ZoomStep);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                Distance = Mathf.Min(MaxDistance, Distance * ZoomStep);
                break;
            case InputEventMouseMotion motion when _turning:
                Yaw = Focus is null ? Mathf.Wrap(Yaw + (motion.Relative.X * Sensitivity), -Mathf.Pi, Mathf.Pi) : Yaw;
                Pitch = Mathf.Clamp(Pitch + (motion.Relative.Y * Sensitivity), MinPitch, MaxPitch);
                break;
            default:
                return;
        }

        GetViewport().SetInputAsHandled();
    }

    /// <summary>Puts <paramref name="camera"/> on the orbit around <paramref name="target"/> and points it at the character.</summary>
    public void Place(Camera3D camera, Vector3 target)
    {
        ArgumentNullException.ThrowIfNull(camera);
        if (Focus is Vector3 focus && new Vector2(focus.X - target.X, focus.Z - target.Z).LengthSquared() > 0.01f)
        {
            // Behind the character, on the far side from the target (yaw 0 = camera on −Z looking along +Z).
            float wanted = Mathf.Atan2(-(focus.X - target.X), focus.Z - target.Z);
            Yaw = Mathf.Wrap(Mathf.LerpAngle(Yaw, wanted, FocusFollow), -Mathf.Pi, Mathf.Pi);
        }

        float flat = Mathf.Cos(Pitch) * Distance;
        camera.Position = target + new Vector3(Mathf.Sin(Yaw) * flat, Mathf.Sin(Pitch) * Distance, -Mathf.Cos(Yaw) * flat);
        camera.LookAt(target + new Vector3(0, LookHeight, 0), Vector3.Up);
    }

    /// <summary>For the self-test: a right-button drag of <paramref name="pixels"/> to the right, through the real input path.</summary>
    public static void DragForTest(float pixels)
    {
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
        Input.ParseInputEvent(new InputEventMouseMotion { Relative = new Vector2(pixels, 0) });
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
    }
}
