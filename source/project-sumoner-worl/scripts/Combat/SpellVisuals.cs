using Godot;
using SumonerWorld.Core.Combat;
using SumonerWorld.Game.Characters;

namespace SumonerWorld.Game.Combat;

/// <summary>
/// The status effects on a body (combat spec §7, §11, M2-11, E3a), shaped so they read without colour too (style guide §2.2):
/// roots are spikes rising around the feet, slowed a low ring of drifting marks, mending a ring of light rising and falling,
/// burning flickering flames, quickened a fast ring of sparks at the waist, weakened marks sinking over the head, warded a dim
/// veil around the body and a shield a glowing ring of plates.
/// </summary>
public sealed partial class StatusMarksView : Node3D
{
    private readonly Node3D _roots;
    private readonly Node3D _slow;
    private readonly Node3D _mend;
    private readonly Node3D _burn;
    private readonly Node3D _quick;
    private readonly Node3D _weak;
    private readonly MeshInstance3D _veil;
    private readonly Node3D _shield;
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
        _burn = Ring("Burning", 6, radius * 0.7f, 0.5f, new CylinderMesh { TopRadius = 0, BottomRadius = 0.1f, Height = 0.4f, RadialSegments = 4 }, Sigil.SchoolColour("ember"));
        _quick = Ring("Quickened", 8, radius * 1.05f, 0.9f, new BoxMesh { Size = new Vector3(0.06f, 0.06f, 0.18f) }, Sigil.SchoolColour("storm"));
        _weak = Ring("Weakened", 3, radius * 0.4f, 2.1f, new CylinderMesh { TopRadius = 0.09f, BottomRadius = 0, Height = 0.25f, RadialSegments = 4 }, Sigil.SchoolColour("umbral"));
        StandardMaterial3D veil = Sigil.Material(new Color(0.3f, 0.2f, 0.45f));
        veil.AlbedoColor = new Color(0.3f, 0.2f, 0.45f, 0.25f);
        _veil = new MeshInstance3D { Name = "Warded", Visible = false, Mesh = new SphereMesh { Radius = radius * 1.3f, Height = radius * 2.6f }, MaterialOverride = veil, Position = new Vector3(0, radius, 0) };
        AddChild(_veil);
        _shield = Ring("Shielded", 6, radius * 1.2f, 0.8f, new BoxMesh { Size = new Vector3(0.35f, 0.4f, 0.04f) }, Sigil.SchoolColour("ember"));
    }

    // A ring of count marks around the body at the given height, hidden at first.
    private Node3D Ring(string name, int count, float radius, float height, Mesh mesh, Color colour)
    {
        var ring = new Node3D { Name = name, Visible = false };
        StandardMaterial3D material = Sigil.Material(colour);
        for (int i = 0; i < count; i++)
        {
            float angle = i * Mathf.Tau / count;
            ring.AddChild(new MeshInstance3D
            {
                Mesh = mesh,
                MaterialOverride = material,
                Position = new Vector3(Mathf.Sin(angle) * radius, height, Mathf.Cos(angle) * radius),
                Rotation = new Vector3(0, angle, 0),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }

        AddChild(ring);
        return ring;
    }

    /// <summary>Shows the effects in <paramref name="marks"/>.</summary>
    public void Show(StatusMarks marks)
    {
        _roots.Visible = marks.HasFlag(StatusMarks.Rooted);
        _slow.Visible = marks.HasFlag(StatusMarks.Slowed);
        _mend.Visible = marks.HasFlag(StatusMarks.Mending);
        _burn.Visible = marks.HasFlag(StatusMarks.Burning);
        _quick.Visible = marks.HasFlag(StatusMarks.Quickened);
        _weak.Visible = marks.HasFlag(StatusMarks.Weakened);
        _veil.Visible = marks.HasFlag(StatusMarks.Warded);
        _shield.Visible = marks.HasFlag(StatusMarks.Shielded);
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        _clock += delta;
        float t = (float)_clock;
        _slow.Rotation = new Vector3(0, t * 0.8f, 0);
        _mend.Position = new Vector3(0, 0.2f + (Mathf.PingPong(t, 1f) * 1.4f), 0);
        _burn.Scale = new Vector3(1, 0.7f + (0.4f * Mathf.Abs(Mathf.Sin(t * 9))), 1);
        _quick.Rotation = new Vector3(0, t * 5f, 0);
        _weak.Position = new Vector3(0, -Mathf.PingPong(t * 0.5f, 0.3f), 0);
        _shield.Rotation = new Vector3(0, -t * 0.6f, 0);
    }
}

/// <summary>A bolt's flight: a thin streak in its school's colour from the caster that fades in a quarter of a second.</summary>
public sealed partial class BoltStreak : Node3D
{
    private const double Seconds = 0.25;
    private readonly StandardMaterial3D _material;
    private readonly Color _colour;
    private double _age;

    /// <summary>
    /// A streak <paramref name="length"/> metres long from <paramref name="from"/> along heading <paramref name="yaw"/>, Primal unless
    /// <paramref name="colour"/> is given.
    /// </summary>
    public BoltStreak(Vector3 from, float yaw, float length, Color? colour = null)
    {
        _colour = colour ?? Sigil.Primal;
        _material = Sigil.Material(_colour);
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

        _material.AlbedoColor = new Color(_colour, 1 - (float)(_age / Seconds));
    }
}
