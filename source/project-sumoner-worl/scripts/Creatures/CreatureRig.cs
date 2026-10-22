using Godot;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Progression;

namespace SumonerWorld.Game.Creatures;

/// <summary>
/// A placeholder creature built from low-poly primitives (style guide §4 and §5: faceted, palette colours, 1 unit = 1 m), with the
/// pivots an <see cref="IEmotePresenter"/> poses: body (height, pitch, roll), head (pitch, tilt, turn), tail, legs and the
/// species' signature feature, which Grudge raises. Since E3b the species' <see cref="LookDefinition"/> sets the colours, size and
/// feature, and its <see cref="BodyType"/> the shape: a <b>quadruped</b> (round grazer or lean hunter), an <b>avian</b> (round body,
/// beak, two legs, wings that spread) or a <b>biped</b> (upright, a skeleton with <c>bones</c>, a block figure without). A spirit's
/// look glows and is a little see-through. Real models replace these with the same pivots.
/// </summary>
public sealed partial class CreatureRig : Node3D
{
    private const float YoungSize = 0.62f;
    private const float ElderSize = 1.15f;

    private CreatureRig()
    {
    }

    /// <summary>Which role's proportions it uses (lean predator or round grazer).</summary>
    public CreatureRole Role { get; private set; }

    /// <summary>The signature feature (look), which the presenter raises: crest, ears, horns, wings, bones, flame, claws or none.</summary>
    public string Feature { get; private set; } = "none";

    /// <summary>Shoulder height in metres (scales pose offsets).</summary>
    public float Height { get; private set; }

    /// <summary>Body pivot (at the body centre).</summary>
    public Node3D Body { get; private set; } = null!;

    /// <summary>Head pivot (at the neck).</summary>
    public Node3D Head { get; private set; } = null!;

    /// <summary>Tail pivot (at the tail root).</summary>
    public Node3D Tail { get; private set; } = null!;

    /// <summary>Signature feature pivot.</summary>
    public Node3D Signature { get; private set; } = null!;

    /// <summary>Leg pivots (hips and shoulders): four for a quadruped (front left, front right, back left, back right), two otherwise.</summary>
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

    /// <summary>Overall size for the stage (1 = grown), times the look's scale.</summary>
    public float Size => BaseScale * Stage switch
    {
        LifeStage.Young => YoungSize,
        LifeStage.Elder => ElderSize,
        _ => 1f,
    };

    /// <summary>The look's scale (1 = the body type's usual size).</summary>
    public float BaseScale { get; private set; } = 1f;

    /// <summary>The elder's mane: pale spikes around the neck.</summary>
    public Node3D Mane { get; private set; } = null!;

    /// <summary>Shows a life stage (size and the elder's mane).</summary>
    public void SetStage(LifeStage stage)
    {
        Stage = stage;
        Mane.Visible = stage == LifeStage.Elder;
    }

    /// <summary>The usual look of a role (grazer look for every non-predator), for the showcases.</summary>
    public static CreatureRig Build(CreatureRole role) =>
        Build(role == CreatureRole.Predator ? CreatureRole.Predator : CreatureRole.Grazer, BodyType.Quadruped, DefaultLook(role));

    /// <summary>The model of a species: its body type and look (or its role's usual look).</summary>
    public static CreatureRig Build(SpeciesDefinition species)
    {
        ArgumentNullException.ThrowIfNull(species);
        return Build(species.Role, species.BodyType, species.Look ?? DefaultLook(species.Role));
    }

    private static CreatureRig Build(CreatureRole role, BodyType body, LookDefinition look)
    {
        var palette = new Palette(Material(look.Colour, look.Glow), Material(look.Dark, look.Glow), Material(look.Accent, look.Glow, emissive: look.Glow || look.Feature == "flame"));
        bool predator = role is CreatureRole.Predator or CreatureRole.Spirit;
        var rig = new CreatureRig { Name = body.ToString(), Role = predator ? CreatureRole.Predator : CreatureRole.Grazer, Feature = look.Feature, BaseScale = look.Scale };
        switch (body)
        {
            case BodyType.Avian:
                BuildAvian(rig, palette, look);
                break;
            case BodyType.Biped:
                BuildBiped(rig, palette, look);
                break;
            default:
                BuildQuadruped(rig, palette, look, predator);
                break;
        }

        rig.Current = Pose.Neutral;
        rig.Mane.Visible = false;
        return rig;
    }

    private static void BuildQuadruped(CreatureRig rig, Palette palette, LookDefinition look, bool predator)
    {
        Proportions p = predator ? Proportions.Predator : Proportions.Grazer;
        p = p with { BodyLength = p.BodyLength * look.Length, TailLength = look.Feature == "flame" ? p.TailLength * 1.4f : p.TailLength };
        rig.Height = p.LegLength + p.BodyRadius;
        rig.BodyRest = new Vector3(0, p.LegLength + (p.BodyRadius * 0.8f), 0);
        rig.Body = Pivot(rig, "Body", rig.BodyRest);
        Part(rig.Body, new CapsuleMesh { Radius = p.BodyRadius, Height = Mathf.Max(p.BodyLength, p.BodyRadius * 2.1f), RadialSegments = 8, Rings = 2 }, palette.Skin, Vector3.Zero, new Vector3(Mathf.Pi / 2, 0, 0));

        float front = p.BodyLength / 2;
        rig.Head = Pivot(rig.Body, "Head", new Vector3(0, p.BodyRadius * 0.5f, front - (p.BodyRadius * 0.3f)));
        Part(rig.Head, new CylinderMesh { TopRadius = p.HeadWidth * 0.32f, BottomRadius = p.HeadWidth * 0.42f, Height = p.HeadLength * 0.8f, RadialSegments = 6 }, palette.Skin, new Vector3(0, p.HeadWidth * 0.1f, p.HeadLength * 0.1f), new Vector3(Mathf.Pi / 2, 0, 0));
        Part(rig.Head, new BoxMesh { Size = new Vector3(p.HeadWidth, p.HeadWidth * 0.9f, p.HeadLength) }, palette.Skin, new Vector3(0, p.HeadWidth * 0.3f, p.HeadLength * 0.55f), Vector3.Zero);
        Part(rig.Head, new BoxMesh { Size = new Vector3(p.HeadWidth * 0.6f, p.HeadWidth * 0.5f, p.HeadLength * 0.5f) }, palette.Dark, new Vector3(0, p.HeadWidth * 0.1f, p.HeadLength * 1.05f), Vector3.Zero);
        Eyes(rig.Head, palette.Dark, p.HeadWidth, new Vector3(p.HeadWidth * 0.42f, p.HeadWidth * 0.45f, p.HeadLength * 0.85f));

        rig.Signature = QuadrupedFeature(rig, palette.Accent, p, look.Feature);
        rig.Tail = Pivot(rig.Body, "Tail", new Vector3(0, p.BodyRadius * 0.4f, -front));
        Part(rig.Tail, new CylinderMesh { TopRadius = p.TailRadius * 0.4f, BottomRadius = p.TailRadius, Height = p.TailLength, RadialSegments = 5 }, predator ? palette.Accent : palette.Dark, new Vector3(0, 0, -p.TailLength / 2), new Vector3(Mathf.Pi / 2, 0, 0));
        if (look.Feature == "flame")
        {
            Part(rig.Tail, new CylinderMesh { TopRadius = 0, BottomRadius = p.TailRadius * 1.4f, Height = p.TailRadius * 4, RadialSegments = 5 }, palette.Accent, new Vector3(0, p.TailRadius * 1.5f, -p.TailLength), Vector3.Zero);
        }

        var legs = new Node3D[4];
        for (int i = 0; i < 4; i++)
        {
            float x = (i % 2 == 0 ? -1 : 1) * p.BodyRadius * 0.6f;
            float z = (i < 2 ? 1 : -1) * p.BodyLength * 0.35f;
            legs[i] = Pivot(rig, $"Leg{i}", new Vector3(x, p.LegLength, z));
            Part(legs[i], new CylinderMesh { TopRadius = p.LegRadius, BottomRadius = p.LegRadius * 0.7f, Height = p.LegLength, RadialSegments = 5 }, palette.Dark, new Vector3(0, -p.LegLength / 2, 0), Vector3.Zero);
            if (look.Feature == "claws" && i < 2)
            {
                Part(legs[i], new CylinderMesh { TopRadius = 0, BottomRadius = p.LegRadius * 0.9f, Height = p.LegRadius * 3, RadialSegments = 4 }, palette.Accent, new Vector3(0, -p.LegLength + (p.LegRadius * 0.5f), p.LegRadius * 1.5f), new Vector3(Mathf.Pi / 2, 0, 0));
            }
        }

        rig.Legs = legs;
        rig.Mane = BuildMane(rig.Body, p.BodyRadius, new Vector3(0, p.BodyRadius * 0.4f, front - (p.BodyRadius * 0.5f)));
    }

    // The quadruped's signature: ears or horns on the head; a crest, flames or ribs along the back.
    private static Node3D QuadrupedFeature(CreatureRig rig, Material accent, Proportions p, string feature)
    {
        if (feature is "ears" or "horns")
        {
            Node3D head = Pivot(rig.Head, feature == "ears" ? "Ears" : "Horns", new Vector3(0, p.HeadWidth * 0.75f, p.HeadLength * 0.35f));
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 tilt = feature == "horns" ? new Vector3(0.9f, 0, side * -0.5f) : Vector3.Zero;
                Part(head, new CylinderMesh { TopRadius = 0, BottomRadius = p.HeadWidth * 0.2f, Height = p.HeadWidth * 0.7f, RadialSegments = 4 }, accent, new Vector3(side * p.HeadWidth * 0.3f, p.HeadWidth * 0.3f, 0), tilt);
            }

            return head;
        }

        Node3D back = Pivot(rig.Body, feature == "flame" ? "Flames" : "Crest", new Vector3(0, p.BodyRadius * 0.85f, 0));
        if (feature is "none" or "claws")
        {
            return back;
        }

        for (int i = -2; i <= 2; i++)
        {
            float height = feature == "flame" ? p.BodyRadius * (0.9f - (Mathf.Abs(i) * 0.15f)) : p.BodyRadius * 0.7f;
            Part(back, new CylinderMesh { TopRadius = 0, BottomRadius = p.BodyRadius * 0.28f, Height = height, RadialSegments = 5 }, accent, new Vector3(0, height * 0.35f, i * p.BodyLength * 0.18f), Vector3.Zero);
        }

        return back;
    }

    // A round bird: an egg body, a round head with a beak and eyes, a fan tail, two thin legs and wings that spread.
    private static void BuildAvian(CreatureRig rig, Palette palette, LookDefinition look)
    {
        const float body = 0.32f;
        const float leg = 0.32f;
        rig.Height = leg + body;
        rig.BodyRest = new Vector3(0, leg + body, 0);
        rig.Body = Pivot(rig, "Body", rig.BodyRest);
        Part(rig.Body, new SphereMesh { Radius = body, Height = body * 2.4f * look.Length, RadialSegments = 8, Rings = 4 }, palette.Skin, Vector3.Zero, new Vector3(Mathf.Pi / 2.6f, 0, 0));
        rig.Head = Pivot(rig.Body, "Head", new Vector3(0, body * 0.9f, body * 0.6f));
        Part(rig.Head, new SphereMesh { Radius = body * 0.62f, Height = body * 1.24f, RadialSegments = 8, Rings = 4 }, palette.Skin, new Vector3(0, body * 0.3f, 0), Vector3.Zero);
        Part(rig.Head, new CylinderMesh { TopRadius = 0, BottomRadius = body * 0.18f, Height = body * 0.5f, RadialSegments = 4 }, palette.Accent, new Vector3(0, body * 0.2f, body * 0.7f), new Vector3(Mathf.Pi / 2, 0, 0));
        Eyes(rig.Head, palette.Dark, body * 1.1f, new Vector3(body * 0.3f, body * 0.45f, body * 0.45f));
        rig.Signature = Pivot(rig.Body, "Wings", new Vector3(0, body * 0.2f, 0));
        for (int side = -1; side <= 1; side += 2)
        {
            Node3D wing = Pivot(rig.Signature, side < 0 ? "WingLeft" : "WingRight", new Vector3(side * body * 0.85f, 0, 0));
            Part(wing, new BoxMesh { Size = new Vector3(body * 1.6f, body * 0.12f, body * 1.3f) }, palette.Dark, new Vector3(side * body * 0.7f, 0, -body * 0.2f), new Vector3(0, 0, side * -1.1f));
        }

        rig.Tail = Pivot(rig.Body, "Tail", new Vector3(0, -body * 0.2f, -body * 0.9f));
        Part(rig.Tail, new BoxMesh { Size = new Vector3(body * 0.9f, body * 0.1f, body * 0.9f) }, palette.Dark, new Vector3(0, 0, -body * 0.4f), new Vector3(0.4f, 0, 0));
        rig.Legs = new Node3D[2];
        for (int i = 0; i < 2; i++)
        {
            rig.Legs[i] = Pivot(rig, $"Leg{i}", new Vector3((i == 0 ? -1 : 1) * body * 0.35f, leg, 0));
            Part(rig.Legs[i], new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.025f, Height = leg, RadialSegments = 4 }, palette.Accent, new Vector3(0, -leg / 2, 0), Vector3.Zero);
        }

        rig.Mane = BuildMane(rig.Body, body, new Vector3(0, body * 0.7f, body * 0.4f));
    }

    // An upright figure: a skeleton (ribs, a skull) with the bones feature, a block figure (the training construct) without.
    private static void BuildBiped(CreatureRig rig, Palette palette, LookDefinition look)
    {
        const float leg = 0.75f;
        const float torso = 0.7f;
        bool bones = look.Feature == "bones";
        rig.Height = leg + (torso / 2);
        rig.BodyRest = new Vector3(0, leg + (torso / 2), 0);
        rig.Body = Pivot(rig, "Body", rig.BodyRest);
        if (bones)
        {
            Part(rig.Body, new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.04f, Height = torso, RadialSegments = 5 }, palette.Skin, Vector3.Zero, Vector3.Zero);
            for (int i = 0; i < 4; i++)
            {
                Part(rig.Body, new TorusMesh { InnerRadius = 0.13f - (i * 0.015f), OuterRadius = 0.16f - (i * 0.015f), Rings = 8, RingSegments = 4 }, palette.Skin, new Vector3(0, (torso * 0.3f) - (i * 0.11f), 0.02f), Vector3.Zero);
            }
        }
        else
        {
            Part(rig.Body, new BoxMesh { Size = new Vector3(0.6f, torso, 0.38f) }, palette.Skin, Vector3.Zero, Vector3.Zero);
        }

        rig.Head = Pivot(rig.Body, "Head", new Vector3(0, torso * 0.55f, 0));
        Part(rig.Head, bones ? new SphereMesh { Radius = 0.17f, Height = 0.32f, RadialSegments = 8, Rings = 4 } : new BoxMesh { Size = new Vector3(0.32f, 0.3f, 0.3f) }, palette.Skin, new Vector3(0, 0.17f, 0), Vector3.Zero);
        Eyes(rig.Head, bones ? palette.Accent : palette.Dark, 0.3f, new Vector3(0.07f, 0.2f, 0.14f));
        rig.Signature = Pivot(rig.Body, "Arms", new Vector3(0, torso * 0.4f, 0));
        for (int side = -1; side <= 1; side += 2)
        {
            Part(rig.Signature, new CylinderMesh { TopRadius = bones ? 0.03f : 0.09f, BottomRadius = bones ? 0.025f : 0.08f, Height = torso * 0.9f, RadialSegments = 5 }, bones ? palette.Skin : palette.Dark, new Vector3(side * (bones ? 0.2f : 0.4f), -torso * 0.4f, 0), new Vector3(0, 0, side * 0.15f));
        }

        rig.Tail = Pivot(rig.Body, "Tail", new Vector3(0, -torso * 0.5f, -0.1f));
        rig.Legs = new Node3D[2];
        for (int i = 0; i < 2; i++)
        {
            rig.Legs[i] = Pivot(rig, $"Leg{i}", new Vector3((i == 0 ? -1 : 1) * 0.15f, leg, 0));
            Part(rig.Legs[i], new CylinderMesh { TopRadius = bones ? 0.035f : 0.1f, BottomRadius = bones ? 0.03f : 0.09f, Height = leg, RadialSegments = 5 }, bones ? palette.Skin : palette.Dark, new Vector3(0, -leg / 2, 0), Vector3.Zero);
        }

        rig.Mane = BuildMane(rig.Body, 0.25f, new Vector3(0, torso * 0.45f, 0));
    }

    private static void Eyes(Node3D head, Material material, float width, Vector3 at)
    {
        for (int side = -1; side <= 1; side += 2)
        {
            Part(head, new SphereMesh { Radius = width * 0.09f, Height = width * 0.18f, RadialSegments = 6, Rings = 3 }, material, new Vector3(side * at.X, at.Y, at.Z), Vector3.Zero);
        }
    }

    // The elder's mane: pale spikes in a half ring.
    private static Node3D BuildMane(Node3D body, float radius, Vector3 at)
    {
        Node3D mane = Pivot(body, "Mane", at);
        var pale = Material("#E0DBC7", glow: false);
        for (int i = 0; i < 7; i++)
        {
            float angle = (i - 3) * 0.45f;
            Part(mane, new CylinderMesh { TopRadius = 0, BottomRadius = radius * 0.16f, Height = radius * 0.9f, RadialSegments = 4 }, pale,
                new Vector3(Mathf.Sin(angle) * radius * 0.8f, Mathf.Cos(angle) * radius * 0.6f, 0), new Vector3(-0.6f, 0, -angle));
        }

        return mane;
    }

    // A role's look before species had their own (E3b): the slice's grazer and predator colours.
    private static LookDefinition DefaultLook(CreatureRole role) => role == CreatureRole.Predator
        ? new LookDefinition { Colour = "#B8662E", Dark = "#402B1F", Accent = "#E6D1B3", Feature = "ears" }
        : new LookDefinition { Colour = "#857860", Dark = "#4D4233", Accent = "#54853F", Feature = "crest" };

    private static Node3D Pivot(Node3D parent, string name, Vector3 position)
    {
        var pivot = new Node3D { Name = name, Position = position };
        parent.AddChild(pivot);
        return pivot;
    }

    private static void Part(Node3D parent, Mesh mesh, Material material, Vector3 position, Vector3 rotation) =>
        parent.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = material, Position = position, Rotation = rotation });

    // A flat colour; a spirit's parts glow and let a little light through, a flame's accent glows.
    private static StandardMaterial3D Material(string html, bool glow, bool emissive = false)
    {
        Color colour = Color.FromHtml(html);
        var material = new StandardMaterial3D { AlbedoColor = glow ? new Color(colour, 0.75f) : colour, Roughness = 0.9f };
        if (glow)
        {
            material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        }

        if (glow || emissive)
        {
            material.EmissionEnabled = true;
            material.Emission = colour;
            material.EmissionEnergyMultiplier = emissive ? 1.2f : 0.4f;
        }

        return material;
    }

    private readonly record struct Palette(Material Skin, Material Dark, Material Accent);

    private readonly record struct Proportions(
        float BodyRadius, float BodyLength, float LegLength, float LegRadius, float HeadWidth, float HeadLength, float TailLength, float TailRadius)
    {
        public static Proportions Grazer => new(0.75f, 2.6f, 0.9f, 0.16f, 0.6f, 0.75f, 0.6f, 0.12f);

        public static Proportions Predator => new(0.32f, 1.5f, 0.55f, 0.08f, 0.34f, 0.5f, 1.0f, 0.12f);
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
