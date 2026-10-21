using Godot;

namespace SumonerWorld.Game.Characters;

/// <summary>
/// A summoning sigil on the ground (M2-06, style guide §2.2: Primal green, organic, the brightest thing on screen): two glowing
/// rings and four marks between them. <see cref="Build"/> makes the one a casting summoner stands in; a <see cref="SigilFlash"/>
/// marks where a creature arrives.
/// </summary>
public static class Sigil
{
    /// <summary>Primal school colour (style guide §2.2, #B8E05A).</summary>
    public static readonly Color Primal = new(0.72f, 0.88f, 0.35f);

    private const float Energy = 4f;

    /// <summary>Builds a sigil of <paramref name="radius"/> metres, lying flat just above the ground.</summary>
    public static Node3D Build(float radius)
    {
        var root = new Node3D { Name = "Sigil" };
        StandardMaterial3D glow = Material();
        root.AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = radius * 0.9f, OuterRadius = radius, Rings = 32, RingSegments = 4 },
            MaterialOverride = glow,
            Position = new Vector3(0, 0.03f, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        root.AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = radius * 0.5f, OuterRadius = radius * 0.56f, Rings = 24, RingSegments = 4 },
            MaterialOverride = glow,
            Position = new Vector3(0, 0.03f, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        for (int i = 0; i < 4; i++)
        {
            float angle = i * Mathf.Pi / 2;
            root.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(radius * 0.08f, 0.02f, radius * 0.32f) },
                MaterialOverride = glow,
                Position = new Vector3(Mathf.Sin(angle) * radius * 0.73f, 0.03f, Mathf.Cos(angle) * radius * 0.73f),
                Rotation = new Vector3(0, angle, 0),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }

        return root;
    }

    /// <summary>The sigils' glowing material (transparent so a flash can fade).</summary>
    public static StandardMaterial3D Material() => new()
    {
        AlbedoColor = Primal,
        EmissionEnabled = true,
        Emission = Primal,
        EmissionEnergyMultiplier = Energy,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
    };
}

/// <summary>A sigil that flares up where a summoned creature arrives, grows and fades out over <see cref="Seconds"/>, then frees itself.</summary>
public sealed partial class SigilFlash : Node3D
{
    /// <summary>How long the flash lasts.</summary>
    public const double Seconds = 1.5;

    private readonly float _radius;
    private double _age;

    /// <summary>Creates a flash for a creature of about <paramref name="radius"/> metres.</summary>
    public SigilFlash(float radius)
    {
        _radius = radius;
        Name = "SigilFlash";
    }

    /// <inheritdoc />
    public override void _Ready() => AddChild(Sigil.Build(_radius));

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        _age += delta;
        float t = (float)(_age / Seconds);
        if (t >= 1)
        {
            QueueFree();
            return;
        }

        Scale = Vector3.One * (0.6f + (0.6f * t));
        RotateY((float)delta * 1.5f);
        foreach (Node child in GetChild(0).GetChildren())
        {
            if (child is MeshInstance3D { MaterialOverride: StandardMaterial3D material })
            {
                material.AlbedoColor = new Color(Sigil.Primal, 1 - t);
            }
        }
    }
}
