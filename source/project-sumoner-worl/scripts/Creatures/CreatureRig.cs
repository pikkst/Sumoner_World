using Godot;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Progression;

namespace SumonerWorld.Game.Creatures;

/// <summary>
/// A placeholder quadruped built from low-poly primitives (style guide §4 and §5: faceted, palette colours, 1 unit = 1 m), with the
/// pivots an <see cref="IEmotePresenter"/> poses: body (height, pitch, roll), head (pitch, tilt, turn), tail, four legs and the
/// species' signature feature (the grazer's moss crest, the predator's ears), which Grudge raises. Two variants for M1:
/// a large, round <b>grazer</b> (mossback) and a lean, long-tailed <b>predator</b> (bogfox). Real models replace these with the same pivots.
/// </summary>
public sealed partial class CreatureRig : Node3D
{
    private CreatureRig()
    {
    }

    /// <summary>Which variant this is.</summary>
    public CreatureRole Role { get; private set; }

    /// <summary>Shoulder height in metres (scales pose offsets).</summary>
    public float Height { get; private set; }

    /// <summary>Body pivot (at the body centre).</summary>
    public Node3D Body { get; private set; } = null!;

    /// <summary>Head pivot (at the neck).</summary>
    public Node3D Head { get; private set; } = null!;

    /// <summary>Tail pivot (at the tail root).</summary>
    public Node3D Tail { get; private set; } = null!;

    /// <summary>Signature feature pivot (crest or ears).</summary>
    public Node3D Signature { get; private set; } = null!;

    /// <summary>Leg pivots (hips and shoulders): front left, front right, back left, back right.</summary>
    public Node3D[] Legs { get; private set; } = [];

    /// <summary>Where the body pivot rests.</summary>
    public Vector3 BodyRest { get; private set; }

    /// <summary>The rig's own animation time in seconds (start it at different values so herds do not move in step).</summary>
    public double Clock { get; set; }

    /// <summary>Gait state used by the presenter (radians).</summary>
    public float GaitPhase { get; set; }

    /// <summary>Current pose, eased toward the emote's pose by the presenter.</summary>
    public Pose Current { get; set; }

    /// <summary>Life stage shown (M2-09): a young one is small, an elder bigger and wears its mane.</summary>
    public LifeStage Stage { get; private set; } = LifeStage.Grown;

    /// <summary>Overall size for the stage (1 = grown).</summary>
    public float Size => Stage switch
    {
        LifeStage.Young => YoungSize,
        LifeStage.Elder => ElderSize,
        _ => 1f,
    };

    /// <summary>The elder's mane: pale spikes around the neck.</summary>
    public Node3D Mane { get; private set; } = null!;

    /// <summary>Shows a life stage (size and the elder's mane).</summary>
    public void SetStage(LifeStage stage)
    {
        Stage = stage;
        Mane.Visible = stage == LifeStage.Elder;
    }

    private const float YoungSize = 0.62f;
    private const float ElderSize = 1.15f;

    /// <summary>Builds the model for a species role (grazer look for every non-predator).</summary>
    public static CreatureRig Build(CreatureRole role)
    {
        bool predator = role == CreatureRole.Predator;
        var p = predator ? Proportions.Predator : Proportions.Grazer;
        var rig = new CreatureRig { Name = predator ? "Predator" : "Grazer", Role = predator ? CreatureRole.Predator : CreatureRole.Grazer, Height = p.LegLength + p.BodyRadius };

        var skin = Material(p.Skin);
        var dark = Material(p.Dark);
        var accent = Material(p.Accent);

        rig.BodyRest = new Vector3(0, p.LegLength + (p.BodyRadius * 0.8f), 0);
        rig.Body = Pivot(rig, "Body", rig.BodyRest);
        Part(rig.Body, new CapsuleMesh { Radius = p.BodyRadius, Height = p.BodyLength, RadialSegments = 8, Rings = 2 }, skin, Vector3.Zero, new Vector3(Mathf.Pi / 2, 0, 0));

        float front = p.BodyLength / 2;
        rig.Head = Pivot(rig.Body, "Head", new Vector3(0, p.BodyRadius * 0.5f, front - (p.BodyRadius * 0.3f)));
        Part(rig.Head, new CylinderMesh { TopRadius = p.HeadWidth * 0.32f, BottomRadius = p.HeadWidth * 0.42f, Height = p.HeadLength * 0.8f, RadialSegments = 6 }, skin, new Vector3(0, p.HeadWidth * 0.1f, p.HeadLength * 0.1f), new Vector3(Mathf.Pi / 2, 0, 0));
        Part(rig.Head, new BoxMesh { Size = new Vector3(p.HeadWidth, p.HeadWidth * 0.9f, p.HeadLength) }, skin, new Vector3(0, p.HeadWidth * 0.3f, p.HeadLength * 0.55f), Vector3.Zero);
        Part(rig.Head, new BoxMesh { Size = new Vector3(p.HeadWidth * 0.6f, p.HeadWidth * 0.5f, p.HeadLength * 0.5f) }, dark, new Vector3(0, p.HeadWidth * 0.1f, p.HeadLength * 1.05f), Vector3.Zero);
        for (int side = -1; side <= 1; side += 2)
        {
            Part(rig.Head, new SphereMesh { Radius = p.HeadWidth * 0.09f, Height = p.HeadWidth * 0.18f, RadialSegments = 6, Rings = 3 }, dark, new Vector3(side * p.HeadWidth * 0.42f, p.HeadWidth * 0.45f, p.HeadLength * 0.85f), Vector3.Zero);
        }

        if (predator)
        {
            rig.Signature = Pivot(rig.Head, "Ears", new Vector3(0, p.HeadWidth * 0.75f, p.HeadLength * 0.35f));
            for (int side = -1; side <= 1; side += 2)
            {
                Part(rig.Signature, new CylinderMesh { TopRadius = 0, BottomRadius = p.HeadWidth * 0.2f, Height = p.HeadWidth * 0.7f, RadialSegments = 4 }, accent, new Vector3(side * p.HeadWidth * 0.3f, p.HeadWidth * 0.3f, 0), Vector3.Zero);
            }
        }
        else
        {
            rig.Signature = Pivot(rig.Body, "Crest", new Vector3(0, p.BodyRadius * 0.85f, 0));
            for (int i = -2; i <= 2; i++)
            {
                Part(rig.Signature, new CylinderMesh { TopRadius = 0, BottomRadius = p.BodyRadius * 0.28f, Height = p.BodyRadius * 0.7f, RadialSegments = 5 }, accent, new Vector3(0, p.BodyRadius * 0.25f, i * p.BodyLength * 0.18f), Vector3.Zero);
            }
        }

        rig.Tail = Pivot(rig.Body, "Tail", new Vector3(0, p.BodyRadius * 0.4f, -front));
        Part(rig.Tail, new CylinderMesh { TopRadius = p.TailRadius * 0.4f, BottomRadius = p.TailRadius, Height = p.TailLength, RadialSegments = 5 }, predator ? accent : dark, new Vector3(0, 0, -p.TailLength / 2), new Vector3(Mathf.Pi / 2, 0, 0));

        var legs = new Node3D[4];
        for (int i = 0; i < 4; i++)
        {
            float x = (i % 2 == 0 ? -1 : 1) * p.BodyRadius * 0.6f;
            float z = (i < 2 ? 1 : -1) * p.BodyLength * 0.35f;
            legs[i] = Pivot(rig, $"Leg{i}", new Vector3(x, p.LegLength, z));
            Part(legs[i], new CylinderMesh { TopRadius = p.LegRadius, BottomRadius = p.LegRadius * 0.7f, Height = p.LegLength, RadialSegments = 5 }, dark, new Vector3(0, -p.LegLength / 2, 0), Vector3.Zero);
        }

        rig.Legs = legs;
        rig.Current = Pose.Neutral;
        rig.Mane = Pivot(rig.Body, "Mane", new Vector3(0, p.BodyRadius * 0.4f, front - (p.BodyRadius * 0.5f)));
        var pale = Material(new Color(0.88f, 0.86f, 0.78f));
        for (int i = 0; i < 7; i++)
        {
            float angle = (i - 3) * 0.45f;
            Part(rig.Mane, new CylinderMesh { TopRadius = 0, BottomRadius = p.BodyRadius * 0.16f, Height = p.BodyRadius * 0.9f, RadialSegments = 4 }, pale,
                new Vector3(Mathf.Sin(angle) * p.BodyRadius * 0.8f, Mathf.Cos(angle) * p.BodyRadius * 0.6f, 0), new Vector3(-0.6f, 0, -angle));
        }

        rig.Mane.Visible = false;
        return rig;
    }

    private static Node3D Pivot(Node3D parent, string name, Vector3 position)
    {
        var pivot = new Node3D { Name = name, Position = position };
        parent.AddChild(pivot);
        return pivot;
    }

    private static void Part(Node3D parent, Mesh mesh, Material material, Vector3 position, Vector3 rotation) =>
        parent.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = material, Position = position, Rotation = rotation });

    private static StandardMaterial3D Material(Color colour) => new() { AlbedoColor = colour, Roughness = 0.9f };

    private readonly record struct Proportions(
        float BodyRadius, float BodyLength, float LegLength, float LegRadius, float HeadWidth, float HeadLength, float TailLength, float TailRadius, Color Skin, Color Dark, Color Accent)
    {
        public static Proportions Grazer => new(0.75f, 2.6f, 0.9f, 0.16f, 0.6f, 0.75f, 0.6f, 0.12f, new Color(0.52f, 0.47f, 0.36f), new Color(0.3f, 0.26f, 0.2f), new Color(0.33f, 0.52f, 0.25f));

        public static Proportions Predator => new(0.32f, 1.5f, 0.55f, 0.08f, 0.34f, 0.5f, 1.0f, 0.12f, new Color(0.72f, 0.4f, 0.18f), new Color(0.25f, 0.17f, 0.12f), new Color(0.9f, 0.82f, 0.7f));
    }
}

/// <summary>A creature pose: offsets and angles the rig is driven to (radians and metres relative to <see cref="CreatureRig.Height"/>).</summary>
/// <param name="BodyDrop">How far the body sinks, as a share of the shoulder height.</param>
/// <param name="BodyPitch">Body nose up (+) or down (−).</param>
/// <param name="HeadPitch">Head up (+) or down (−).</param>
/// <param name="HeadTilt">Head tilt to one side.</param>
/// <param name="TailPitch">Tail up (+) or tucked (−).</param>
/// <param name="TailWag">Tail wag amplitude.</param>
/// <param name="WagRate">Tail wag cycles per second.</param>
/// <param name="SignatureRaise">0 = resting, 1 = fully raised (Grudge).</param>
/// <param name="Bob">Head bob amplitude (nuzzling, sniffing).</param>
/// <param name="TiltRate">Head tilt oscillation cycles per second (0 = hold the tilt).</param>
/// <param name="GaitScale">Stride length factor (slow, heavy steps below 1).</param>
public readonly record struct Pose(
    float BodyDrop, float BodyPitch, float HeadPitch, float HeadTilt, float TailPitch, float TailWag, float WagRate, float SignatureRaise, float Bob, float TiltRate, float GaitScale)
{
    /// <summary>Calm idle: head down grazing, tail hanging.</summary>
    public static Pose Neutral => new(0, 0, -0.45f, 0, -0.45f, 0.05f, 0.5f, 0, 0, 0, 1);

    /// <summary>Moves every value a share <paramref name="t"/> of the way toward <paramref name="target"/>.</summary>
    public Pose Toward(Pose target, float t) => new(
        Mathf.Lerp(BodyDrop, target.BodyDrop, t),
        Mathf.Lerp(BodyPitch, target.BodyPitch, t),
        Mathf.Lerp(HeadPitch, target.HeadPitch, t),
        Mathf.Lerp(HeadTilt, target.HeadTilt, t),
        Mathf.Lerp(TailPitch, target.TailPitch, t),
        Mathf.Lerp(TailWag, target.TailWag, t),
        Mathf.Lerp(WagRate, target.WagRate, t),
        Mathf.Lerp(SignatureRaise, target.SignatureRaise, t),
        Mathf.Lerp(Bob, target.Bob, t),
        Mathf.Lerp(TiltRate, target.TiltRate, t),
        Mathf.Lerp(GaitScale, target.GaitScale, t));
}
