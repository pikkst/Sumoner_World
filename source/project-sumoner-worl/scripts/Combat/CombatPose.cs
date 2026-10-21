using Godot;
using SumonerWorld.Core.Combat;

namespace SumonerWorld.Game.Combat;

/// <summary>
/// Placeholder combat body language until real animations (combat spec §11): a lean back in the wind-up (the telegraph), a lunge
/// in the strike, a wobble when staggered, lying down when defeated, lowered when yielding, and a short swell when hit. It only
/// tilts and scales the view; position and heading stay with the snapshot.
/// </summary>
public static class CombatPose
{
    private const float Windup = -0.25f;
    private const float Strike = 0.4f;
    private const float Dodge = 0.3f;
    private const float Yield = 0.3f;
    private const float Down = 1.45f;
    private const float WobbleSpeed = 30f;
    private const float WobbleAngle = 0.12f;
    private const float HitSwell = 1.12f;
    private const float SwellRecovery = 6f;

    /// <summary>Sets <paramref name="view"/>'s tilt for <paramref name="action"/> around heading <paramref name="yaw"/>.</summary>
    public static void Apply(Node3D view, byte action, float yaw, double time)
    {
        ArgumentNullException.ThrowIfNull(view);
        (float pitch, float roll) = (CombatAction)action switch
        {
            CombatAction.Windup => (Windup, 0f),
            CombatAction.Strike => (Strike, 0f),
            CombatAction.Dodge => (Dodge, 0f),
            CombatAction.Yield => (Yield, 0f),
            CombatAction.Stagger => (0f, MathF.Sin((float)time * WobbleSpeed) * WobbleAngle),
            CombatAction.Down => (0f, Down),
            _ => (0f, 0f),
        };
        view.Rotation = new Vector3(pitch, yaw, roll);
    }

    /// <summary>Starts the "hit" swell.</summary>
    public static void Hit(Node3D view)
    {
        ArgumentNullException.ThrowIfNull(view);
        view.Scale = Vector3.One * HitSwell;
    }

    /// <summary>Eases a swell back to normal size.</summary>
    public static void Settle(Node3D view, double delta)
    {
        ArgumentNullException.ThrowIfNull(view);
        view.Scale = view.Scale.Lerp(Vector3.One, Math.Min(1f, (float)delta * SwellRecovery));
    }
}

/// <summary>A small health bar that turns to face the camera every frame, shown over a creature or player while hurt.</summary>
public sealed partial class HealthBar3D : Node3D
{
    private const float Width = 0.9f;
    private const float Height = 0.09f;

    private readonly MeshInstance3D _fill;

    /// <summary>Builds the bar <paramref name="height"/> metres above the view's origin.</summary>
    public HealthBar3D(float height)
    {
        Name = "HealthBar";
        Position = new Vector3(0, height, 0);
        MeshInstance3D back = Quad(Width + 0.04f, Height + 0.04f, new Color(0, 0, 0, 0.6f), 0);
        back.Position = new Vector3(0, 0, -0.01f);
        AddChild(back);
        _fill = Quad(Width, Height, new Color(0.85f, 0.25f, 0.2f), 1);
        AddChild(_fill);
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (Visible && GetViewport().GetCamera3D() is Camera3D camera)
        {
            // +Z towards the camera, so the bar's X axis is the camera's right and the fill grows from the left.
            LookAt((GlobalPosition * 2) - camera.GlobalPosition, Vector3.Up);
        }
    }

    /// <summary>Shows the fraction (0–255); hidden at full health.</summary>
    public void SetHealth(byte health)
    {
        Visible = health < byte.MaxValue;
        float share = health / 255f;
        _fill.Scale = new Vector3(Math.Max(0.001f, share), 1, 1);
        _fill.Position = new Vector3(-Width * (1 - share) / 2f, 0, 0);
    }

    private static MeshInstance3D Quad(float width, float height, Color colour, int priority) => new()
    {
        Mesh = new QuadMesh { Size = new Vector2(width, height) },
        MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = colour,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            NoDepthTest = true,
            RenderPriority = priority,
        },
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };
}
