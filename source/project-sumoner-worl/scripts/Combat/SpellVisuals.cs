using Godot;
using SumonerWorld.Core.Combat;
using SumonerWorld.Game.Characters;

namespace SumonerWorld.Game.Combat;

/// <summary>
/// The status effects on a body (combat spec §7, §11, M2-11), shaped so they read without colour too (style guide §2.2): roots
/// are spikes rising around the feet, slowed a low ring of drifting marks, mending a ring of light rising and falling.
/// </summary>
public sealed partial class StatusMarksView : Node3D
{
    private readonly Node3D _roots;
    private readonly Node3D _slow;
    private readonly Node3D _mend;
    private double _clock;

    /// <summary>Builds the marks for a body of about <paramref name="radius"/> metres.</summary>
    public StatusMarksView(float radius)
    {
        Name = "StatusMarks";
        StandardMaterial3D primal = Sigil.Material();
        _roots = new Node3D { Name = "Roots", Visible = false };
        for (int i = 0; i < 7; i++)
        {
            float angle = i * Mathf.Tau / 7;
            _roots.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0, BottomRadius = 0.08f, Height = 0.7f, RadialSegments = 4 },
                MaterialOverride = primal,
                Position = new Vector3(Mathf.Sin(angle) * radius, 0.3f, Mathf.Cos(angle) * radius),
                Rotation = new Vector3(Mathf.Cos(angle) * 0.3f, 0, -Mathf.Sin(angle) * 0.3f),
            });
        }

        AddChild(_roots);
        _slow = new Node3D { Name = "Slowed", Visible = false };
        var slowMaterial = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.75f, 0.95f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        for (int i = 0; i < 5; i++)
        {
            float angle = i * Mathf.Tau / 5;
            _slow.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.12f, 0.04f, 0.3f) },
                MaterialOverride = slowMaterial,
                Position = new Vector3(Mathf.Sin(angle) * radius * 1.1f, 0.1f, Mathf.Cos(angle) * radius * 1.1f),
                Rotation = new Vector3(0, angle, 0),
            });
        }

        AddChild(_slow);
        _mend = new Node3D { Name = "Mending", Visible = false };
        _mend.AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = radius * 0.8f, OuterRadius = radius * 0.9f, Rings = 24, RingSegments = 4 },
            MaterialOverride = primal,
        });
        AddChild(_mend);
    }

    /// <summary>Shows the effects in <paramref name="marks"/>.</summary>
    public void Show(StatusMarks marks)
    {
        _roots.Visible = marks.HasFlag(StatusMarks.Rooted);
        _slow.Visible = marks.HasFlag(StatusMarks.Slowed);
        _mend.Visible = marks.HasFlag(StatusMarks.Mending);
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        _clock += delta;
        float t = (float)_clock;
        _slow.Rotation = new Vector3(0, t * 0.8f, 0);
        _mend.Position = new Vector3(0, 0.2f + (Mathf.PingPong(t, 1f) * 1.4f), 0);
    }
}

/// <summary>A thornbolt's flight: a thin Primal streak from the caster that fades in a quarter of a second.</summary>
public sealed partial class BoltStreak : Node3D
{
    private const double Seconds = 0.25;
    private readonly StandardMaterial3D _material = Sigil.Material();
    private double _age;

    /// <summary>A streak <paramref name="length"/> metres long from <paramref name="from"/> along heading <paramref name="yaw"/>.</summary>
    public BoltStreak(Vector3 from, float yaw, float length)
    {
        Name = "BoltStreak";
        Position = from + new Vector3(0, 1.2f, 0);
        Rotation = new Vector3(0, yaw, 0);
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.12f, 0.12f, length) },
            MaterialOverride = _material,
            Position = new Vector3(0, 0, length / 2),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        _age += delta;
        if (_age >= Seconds)
        {
            QueueFree();
            return;
        }

        _material.AlbedoColor = new Color(Sigil.Primal, 1 - (float)(_age / Seconds));
    }
}
