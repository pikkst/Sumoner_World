using Godot;
using SumonerWorld.Core.Combat;

namespace SumonerWorld.Game.Characters;

/// <summary>
/// Procedural animation for the <see cref="HumanoidRig"/> until real clips exist (M2-04, combat spec §11): idle breathing, a walk and
/// a run whose stride follows ground speed, and one pose per combat action, eased over about 0.1 s so changes never pop:
/// <list type="bullet">
/// <item><b>Wind-up</b> (the telegraph): staff raised high behind the head, chest turned away, weight back.</item>
/// <item><b>Strike:</b> staff swung forward and down, chest turned in, lunge with the leading leg.</item>
/// <item><b>Recover:</b> staff low, half-way back to rest.</item>
/// <item><b>Block:</b> both forearms up in front of the face, crouched.</item>
/// <item><b>Dodge:</b> a tucked, low dive forward.</item>
/// <item><b>Stagger:</b> arms thrown out, head back, a wobbly crouch.</item>
/// <item><b>Yield:</b> down on one knee, head bowed, staff lowered.</item>
/// <item><b>Down:</b> limp (the view itself is laid on its side by <c>CombatPose</c>).</item>
/// <item><b>Cast</b> (M2-06): both arms raised to the sky, staff upright and high, standing in a turning, pulsing sigil.</item>
/// <item><b>Hit:</b> a short flinch (head and chest snap back) on top of any pose, started by <see cref="Hit"/>.</item>
/// </list>
/// The whole-body tilts of <c>CombatPose</c> still apply to the view, so both layers together read at a distance.
/// </summary>
public static class HumanoidAnimator
{
    /// <summary>Ground speed above which the run cycle replaces the walk (m/s).</summary>
    public const float RunSpeed = 3.5f;

    private const float EaseRate = 18f;
    private const float WalkStride = 1.3f;
    private const float RunStride = 2.4f;
    private const float StillSpeed = 0.2f;
    private const float FlinchSeconds = 0.25f;

    /// <summary>The pose a combat action drives toward (null = locomotion only).</summary>
    public static HumanPose? PoseOf(CombatAction action) => action switch
    {
        CombatAction.Windup => new(0.05f, -0.2f, -0.55f, -0.1f, -0.5f, 0.3f, -0.4f, -2.7f, 0.25f, -0.6f, -0.25f, 0.25f, 0.2f, 0.15f, 2.4f),
        CombatAction.Strike => new(0.12f, 0.35f, 0.5f, 0.15f, 0.4f, 0.25f, -0.3f, -1.3f, 0.05f, 0, 0.35f, 0.3f, -0.6f, 0.45f, 3.3f),
        CombatAction.Recover => new(0.05f, 0.15f, 0.2f, 0.1f, 0.15f, 0.2f, -0.3f, -0.6f, 0.1f, -0.3f, 0.15f, 0.2f, -0.25f, 0.25f, 1.5f),
        CombatAction.Block => new(0.18f, 0.12f, 0, 0.2f, -1.45f, -0.35f, -1.6f, -1.45f, -0.35f, -1.5f, -0.3f, 0.55f, 0.25f, 0.45f, 2.65f),
        CombatAction.Dodge => new(0.45f, 0.75f, 0, -0.3f, 0.6f, 0.2f, -1.4f, 0.6f, 0.2f, -1.4f, -1.2f, 1.8f, 0.4f, 1.3f, 1f),
        CombatAction.Stagger => new(0.15f, -0.3f, 0.2f, -0.45f, -0.3f, 1.2f, -0.3f, -0.4f, 1.1f, -0.5f, -0.2f, 0.4f, 0.35f, 0.35f, 0.35f),
        CombatAction.Yield => new(0.42f, 0.25f, 0, 0.7f, 0.1f, 0.1f, -0.6f, 0.05f, 0.1f, -0.9f, -1.5f, 1.55f, 0.1f, 2.4f, 2.6f),
        CombatAction.Down => new(0, 0, 0, 0.3f, -0.4f, 1.3f, -0.2f, -0.2f, 1.1f, -0.3f, -0.1f, 0.2f, 0.15f, 0.3f, 0.35f),
        CombatAction.Cast => new(0, -0.15f, 0, -0.35f, -2.6f, 0.35f, -0.2f, -2.5f, 0.3f, -0.3f, -0.15f, 0.1f, 0.15f, 0.1f, 2.8f),
        _ => null,
    };

    /// <summary>Starts the "hit" flinch.</summary>
    public static void Hit(HumanoidRig rig)
    {
        ArgumentNullException.ThrowIfNull(rig);
        rig.Flinch = FlinchSeconds;
    }

    /// <summary>Drives <paramref name="rig"/> one frame toward <paramref name="action"/>'s pose while it moves at <paramref name="speed"/> m/s.</summary>
    public static void Animate(HumanoidRig rig, CombatAction action, float speed, double delta)
    {
        ArgumentNullException.ThrowIfNull(rig);
        rig.Clock += delta;
        float t = (float)rig.Clock;
        float dt = (float)delta;
        HumanPose? combat = PoseOf(action);
        HumanPose target = combat ?? Locomotion(rig, speed, dt);
        HumanPose pose = rig.Current.Toward(target, 1 - Mathf.Exp(-EaseRate * dt));
        rig.Current = pose;

        float breath = combat is null && speed < StillSpeed ? Mathf.Sin(t * 1.8f) * 0.012f : 0;
        float wobble = action == CombatAction.Stagger ? Mathf.Sin(t * 26f) * 0.15f : 0;
        rig.Flinch = Mathf.Max(0, rig.Flinch - dt);
        float flinch = rig.Flinch / FlinchSeconds;

        // A bounce at each footfall when moving; the hips also sink with the pose.
        float bounce = combat is null ? Mathf.Abs(Mathf.Sin(rig.GaitPhase)) * Mathf.Clamp(speed / RunSpeed, 0, 1) * 0.05f : 0;
        rig.Hips.Position = new Vector3(0, HumanoidRig.HipHeight - pose.HipsDrop + breath + bounce, 0);
        rig.Torso.Rotation = new Vector3(pose.Lean - (flinch * 0.35f), pose.Twist, wobble);
        rig.Head.Rotation = new Vector3(pose.HeadPitch - (flinch * 0.5f), 0, -wobble);

        rig.Arms[0].Rotation = new Vector3(pose.LeftArm, 0, pose.LeftArmOut + (flinch * 0.4f));
        rig.Elbows[0].Rotation = new Vector3(pose.LeftElbow, 0, 0);
        rig.Arms[1].Rotation = new Vector3(pose.RightArm, 0, -(pose.RightArmOut + (flinch * 0.4f)));
        rig.Elbows[1].Rotation = new Vector3(pose.RightElbow, 0, 0);
        rig.Legs[0].Rotation = new Vector3(pose.LeftLeg, 0, 0);
        rig.Knees[0].Rotation = new Vector3(pose.LeftKnee, 0, 0);
        rig.Legs[1].Rotation = new Vector3(pose.RightLeg, 0, 0);
        rig.Knees[1].Rotation = new Vector3(pose.RightKnee, 0, 0);
        rig.Staff.Rotation = new Vector3(pose.Staff, 0, 0);
        rig.Sigil.Visible = action == CombatAction.Cast;
        if (rig.Sigil.Visible)
        {
            rig.Sigil.Rotation = new Vector3(0, t * 1.2f, 0);
            rig.Sigil.Scale = Vector3.One * (1 + (Mathf.Sin(t * 8) * 0.05f));
        }
    }

    // Walk and run: legs swing in opposition, arms counter-swing, knees bend on the forward swing; the run leans in and bends more.
    private static HumanPose Locomotion(HumanoidRig rig, float speed, float delta)
    {
        if (speed < StillSpeed)
        {
            return HumanPose.Rest;
        }

        float run = Mathf.Clamp((speed - 2f) / (RunSpeed - 2f), 0, 1);
        float stride = Mathf.Lerp(WalkStride, RunStride, run);
        rig.GaitPhase = Mathf.PosMod(rig.GaitPhase + (speed / stride * Mathf.Pi * delta), Mathf.Tau);
        float swing = Mathf.Sin(rig.GaitPhase);
        float legSwing = Mathf.Lerp(0.45f, 0.8f, run) * Mathf.Clamp(speed / 1.5f, 0.3f, 1);
        float armSwing = Mathf.Lerp(0.35f, 0.75f, run) * Mathf.Clamp(speed / 1.5f, 0.3f, 1);
        float knee = Mathf.Lerp(0.5f, 1.3f, run);
        float elbow = Mathf.Lerp(-0.3f, -1.3f, run);
        return new HumanPose(
            HipsDrop: run * 0.06f,
            Lean: Mathf.Lerp(0.05f, 0.3f, run),
            Twist: swing * 0.12f,
            HeadPitch: Mathf.Lerp(0.05f, -0.1f, run),
            LeftArm: swing * armSwing,
            LeftArmOut: 0.1f,
            LeftElbow: elbow,
            RightArm: -swing * armSwing * 0.6f,
            RightArmOut: 0.1f,
            RightElbow: elbow,
            LeftLeg: -swing * legSwing,
            LeftKnee: 0.1f + (Mathf.Max(0, Mathf.Cos(rig.GaitPhase)) * knee),
            RightLeg: swing * legSwing,
            RightKnee: 0.1f + (Mathf.Max(0, -Mathf.Cos(rig.GaitPhase)) * knee),
            Staff: 0.35f - elbow);
    }
}
