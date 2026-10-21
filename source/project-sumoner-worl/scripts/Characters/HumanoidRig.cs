using Godot;

namespace SumonerWorld.Game.Characters;

/// <summary>
/// A placeholder summoner built from low-poly primitives (style guide §1 silhouette first, §2 player value "like creatures", mid
/// saturation; 1 unit = 1 m, 1.8 m tall, feet at the origin, facing +Z) with the pivots a <see cref="HumanoidAnimator"/> poses:
/// hips, torso, head, two-part arms (shoulder, elbow) and legs (hip, knee), and a staff in the right hand whose tip glows faintly in
/// Primal green (magic is the brightest thing, but only a hint until spells exist). A real model replaces this with the same pivots.
/// </summary>
public sealed partial class HumanoidRig : Node3D
{
    /// <summary>Height of the hip pivot above the feet.</summary>
    public const float HipHeight = 0.92f;

    /// <summary>Upper leg and lower leg length.</summary>
    public const float LegSegment = 0.45f;

    private const float ArmSegment = 0.3f;
    private const float TorsoHeight = 0.55f;
    private const float ShoulderWidth = 0.24f;
    private const float HipWidth = 0.11f;

    private HumanoidRig()
    {
    }

    /// <summary>Hips pivot (moves up and down; everything above hangs from it).</summary>
    public Node3D Hips { get; private set; } = null!;

    /// <summary>Torso pivot (at the waist).</summary>
    public Node3D Torso { get; private set; } = null!;

    /// <summary>Head pivot (at the neck).</summary>
    public Node3D Head { get; private set; } = null!;

    /// <summary>Shoulder pivots: left (+X), right (−X).</summary>
    public Node3D[] Arms { get; private set; } = [];

    /// <summary>Elbow pivots: left, right.</summary>
    public Node3D[] Elbows { get; private set; } = [];

    /// <summary>Hip joint pivots: left, right.</summary>
    public Node3D[] Legs { get; private set; } = [];

    /// <summary>Knee pivots: left, right.</summary>
    public Node3D[] Knees { get; private set; } = [];

    /// <summary>Staff pivot (at the right hand; local +Y runs from the grip to the glowing tip).</summary>
    public Node3D Staff { get; private set; } = null!;

    /// <summary>The rig's own animation time in seconds.</summary>
    public double Clock { get; set; }

    /// <summary>Walk cycle phase (radians).</summary>
    public float GaitPhase { get; set; }

    /// <summary>Current pose, eased toward the target by the animator.</summary>
    public HumanPose Current { get; set; }

    /// <summary>Seconds left of the "hit" flinch.</summary>
    public float Flinch { get; set; }

    /// <summary>Builds the model with <paramref name="cloth"/> as the robe colour.</summary>
    public static HumanoidRig Build(Color cloth)
    {
        var rig = new HumanoidRig { Name = "Summoner", Current = HumanPose.Rest };
        var robe = Material(cloth);
        var trim = Material(new Color(0.78f, 0.66f, 0.38f));
        var skin = Material(new Color(0.85f, 0.68f, 0.52f));
        var dark = Material(new Color(0.24f, 0.2f, 0.17f));
        var wood = Material(new Color(0.45f, 0.32f, 0.2f));

        rig.Hips = Pivot(rig, "Hips", new Vector3(0, HipHeight, 0));
        Part(rig.Hips, new CylinderMesh { TopRadius = 0.2f, BottomRadius = 0.3f, Height = 0.45f, RadialSegments = 8 }, robe, new Vector3(0, -0.12f, 0), Vector3.Zero);

        rig.Torso = Pivot(rig.Hips, "Torso", new Vector3(0, 0.05f, 0));
        Part(rig.Torso, new BoxMesh { Size = new Vector3(0.42f, TorsoHeight, 0.24f) }, robe, new Vector3(0, TorsoHeight / 2, 0), Vector3.Zero);
        Part(rig.Torso, new BoxMesh { Size = new Vector3(0.44f, 0.07f, 0.26f) }, trim, new Vector3(0, 0.04f, 0), Vector3.Zero);
        Part(rig.Torso, new BoxMesh { Size = new Vector3(0.1f, TorsoHeight * 0.9f, 0.02f) }, trim, new Vector3(0, TorsoHeight / 2, 0.125f), Vector3.Zero);

        rig.Head = Pivot(rig.Torso, "Head", new Vector3(0, TorsoHeight + 0.04f, 0));
        Part(rig.Head, new SphereMesh { Radius = 0.13f, Height = 0.28f, RadialSegments = 8, Rings = 4 }, skin, new Vector3(0, 0.15f, 0.01f), Vector3.Zero);
        Part(rig.Head, new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.17f, Height = 0.3f, RadialSegments = 6 }, robe, new Vector3(0, 0.24f, -0.03f), new Vector3(-0.25f, 0, 0));
        for (int side = -1; side <= 1; side += 2)
        {
            Part(rig.Head, new BoxMesh { Size = new Vector3(0.035f, 0.035f, 0.02f) }, dark, new Vector3(side * 0.05f, 0.16f, 0.13f), Vector3.Zero);
        }

        rig.Arms = new Node3D[2];
        rig.Elbows = new Node3D[2];
        rig.Legs = new Node3D[2];
        rig.Knees = new Node3D[2];
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? 1 : -1;
            rig.Arms[i] = Pivot(rig.Torso, i == 0 ? "ArmL" : "ArmR", new Vector3(side * ShoulderWidth, TorsoHeight - 0.05f, 0));
            Part(rig.Arms[i], new CylinderMesh { TopRadius = 0.065f, BottomRadius = 0.055f, Height = ArmSegment, RadialSegments = 6 }, robe, new Vector3(0, -ArmSegment / 2, 0), Vector3.Zero);
            rig.Elbows[i] = Pivot(rig.Arms[i], "Elbow", new Vector3(0, -ArmSegment, 0));
            Part(rig.Elbows[i], new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.045f, Height = ArmSegment, RadialSegments = 6 }, robe, new Vector3(0, -ArmSegment / 2, 0), Vector3.Zero);
            Part(rig.Elbows[i], new BoxMesh { Size = new Vector3(0.07f, 0.09f, 0.08f) }, skin, new Vector3(0, -ArmSegment - 0.04f, 0), Vector3.Zero);

            rig.Legs[i] = Pivot(rig.Hips, i == 0 ? "LegL" : "LegR", new Vector3(side * HipWidth, 0, 0));
            Part(rig.Legs[i], new CylinderMesh { TopRadius = 0.085f, BottomRadius = 0.07f, Height = LegSegment, RadialSegments = 6 }, dark, new Vector3(0, -LegSegment / 2, 0), Vector3.Zero);
            rig.Knees[i] = Pivot(rig.Legs[i], "Knee", new Vector3(0, -LegSegment, 0));
            Part(rig.Knees[i], new CylinderMesh { TopRadius = 0.065f, BottomRadius = 0.055f, Height = LegSegment - 0.05f, RadialSegments = 6 }, dark, new Vector3(0, -(LegSegment - 0.05f) / 2, 0), Vector3.Zero);
            Part(rig.Knees[i], new BoxMesh { Size = new Vector3(0.11f, 0.07f, 0.24f) }, dark, new Vector3(0, -LegSegment + 0.02f, 0.05f), Vector3.Zero);
        }

        // The staff is gripped in the right hand; the pose turns it at the wrist (upright at rest, swung forward in a strike).
        Node3D staff = rig.Staff = Pivot(rig.Elbows[1], "Staff", new Vector3(0, -ArmSegment - 0.04f, 0.02f));
        Part(staff, new CylinderMesh { TopRadius = 0.025f, BottomRadius = 0.03f, Height = 1.4f, RadialSegments = 5 }, wood, new Vector3(0, 0.25f, 0), Vector3.Zero);
        Part(staff, new SphereMesh { Radius = 0.06f, Height = 0.12f, RadialSegments = 6, Rings = 3 }, Glow(new Color(0.72f, 0.88f, 0.35f)), new Vector3(0, 0.98f, 0), Vector3.Zero);
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

    private static StandardMaterial3D Glow(Color colour) => new()
    {
        AlbedoColor = colour,
        EmissionEnabled = true,
        Emission = colour,
        EmissionEnergyMultiplier = 1.5f,
    };
}

/// <summary>
/// A humanoid pose in radians (Godot axes on a rig facing +Z: positive X rotation leans forward / swings a limb back, positive Z
/// rotation tips toward +X, the character's left) and metres.
/// </summary>
/// <param name="HipsDrop">How far the hips sink (crouch, kneel), metres.</param>
/// <param name="Lean">Torso forward (+) or back (−).</param>
/// <param name="Twist">Torso turn: positive turns the chest to the character's left.</param>
/// <param name="HeadPitch">Head down (+) or up (−).</param>
/// <param name="LeftArm">Left shoulder swing: negative raises the arm forward.</param>
/// <param name="LeftArmOut">Left arm out to the side (+).</param>
/// <param name="LeftElbow">Left elbow bend (negative bends the forearm forward).</param>
/// <param name="RightArm">Right shoulder swing: negative raises the arm forward.</param>
/// <param name="RightArmOut">Right arm out to the side (+).</param>
/// <param name="RightElbow">Right elbow bend (negative bends the forearm forward).</param>
/// <param name="LeftLeg">Left hip swing: negative lifts the leg forward.</param>
/// <param name="LeftKnee">Left knee bend (positive).</param>
/// <param name="RightLeg">Right hip swing.</param>
/// <param name="RightKnee">Right knee bend.</param>
/// <param name="Staff">Staff angle at the wrist, added to the right arm and elbow (the staff's tip points along local +Y).</param>
public readonly record struct HumanPose(
    float HipsDrop,
    float Lean,
    float Twist,
    float HeadPitch,
    float LeftArm,
    float LeftArmOut,
    float LeftElbow,
    float RightArm,
    float RightArmOut,
    float RightElbow,
    float LeftLeg,
    float LeftKnee,
    float RightLeg,
    float RightKnee,
    float Staff)
{
    /// <summary>Standing relaxed, arms slightly out, elbows soft.</summary>
    public static HumanPose Rest => new(0, 0.02f, 0, 0.05f, 0, 0.1f, -0.15f, -0.1f, 0.1f, -0.35f, 0, 0.05f, 0, 0.05f, 0.35f);

    /// <summary>Moves every value a share <paramref name="t"/> of the way toward <paramref name="target"/>.</summary>
    public HumanPose Toward(HumanPose target, float t) => new(
        Mathf.Lerp(HipsDrop, target.HipsDrop, t),
        Mathf.Lerp(Lean, target.Lean, t),
        Mathf.Lerp(Twist, target.Twist, t),
        Mathf.Lerp(HeadPitch, target.HeadPitch, t),
        Mathf.Lerp(LeftArm, target.LeftArm, t),
        Mathf.Lerp(LeftArmOut, target.LeftArmOut, t),
        Mathf.Lerp(LeftElbow, target.LeftElbow, t),
        Mathf.Lerp(RightArm, target.RightArm, t),
        Mathf.Lerp(RightArmOut, target.RightArmOut, t),
        Mathf.Lerp(RightElbow, target.RightElbow, t),
        Mathf.Lerp(LeftLeg, target.LeftLeg, t),
        Mathf.Lerp(LeftKnee, target.LeftKnee, t),
        Mathf.Lerp(RightLeg, target.RightLeg, t),
        Mathf.Lerp(RightKnee, target.RightKnee, t),
        Mathf.Lerp(Staff, target.Staff, t));
}
