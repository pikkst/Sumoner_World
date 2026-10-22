using Godot;
using SumonerWorld.Core.Cognition.Appraisal;

namespace SumonerWorld.Game.Creatures;

/// <summary>Shows an emote and movement on a creature (AI Bible §6.2, style guide §4); one implementation per body type.</summary>
public interface IEmotePresenter
{
    /// <summary>Drives <paramref name="rig"/> one frame toward <paramref name="emote"/>'s pose while it moves at <paramref name="speed"/> m/s.</summary>
    void Present(CreatureRig rig, Emote emote, float speed, double delta);
}

/// <summary>
/// The six quadruped emotes from the style guide §4 table, exaggerated to read at 30 m without UI, plus idle and a walk cycle.
/// Poses ease over about 0.3 s so emote changes never pop:
/// <list type="bullet">
/// <item><b>Trust:</b> head level, slow tail wag, body relaxed.</item>
/// <item><b>Fear:</b> body low, weight back (nose up), head pulled back and down, tail tucked, short trembling steps.</item>
/// <item><b>Grudge:</b> head low and forward (stare), signature feature raised (crest up / ears flat back), tail stiff, front low.</item>
/// <item><b>Curiosity:</b> head up and forward with a slow side-to-side tilt, sniffing bob, tail up.</item>
/// <item><b>Affection:</b> head low nuzzling bob, quick tail wag, body leaning in.</item>
/// <item><b>Grief:</b> head very low, body sagging, tail hanging, slow heavy steps.</item>
/// </list>
/// </summary>
public sealed class QuadrupedEmotePresenter : IEmotePresenter
{
    private const float EaseRate = 8f;
    private const float StrideMetres = 1.2f;
    private const float LegSwing = 0.55f;
    private const float WalkThreshold = 0.2f;

    /// <summary>The pose an emote drives toward.</summary>
    public static Pose PoseOf(Emote emote) => emote switch
    {
        Emote.Trust => new(-0.04f, 0.08f, 0.25f, 0, 0.55f, 0.6f, 0.7f, 0, 0, 0, 1),
        Emote.Fear => new(0.42f, 0.3f, -0.1f, 0, -1.3f, 0.08f, 9f, 0, 0, 0, 0.6f),
        Emote.Grudge => new(0.15f, -0.3f, -0.35f, 0, 0.9f, 0, 0, 1, 0, 0, 0.8f),
        Emote.Curiosity => new(-0.05f, 0.12f, 0.6f, 0.65f, 0.6f, 0.25f, 1.5f, 0, 0.08f, 0.45f, 1),
        Emote.Affection => new(0.05f, 0, -0.2f, 0.3f, 0.5f, 0.9f, 3.5f, 0, 0.2f, 0, 1),
        Emote.Grief => new(0.25f, -0.2f, -1.25f, 0, -1.1f, 0, 0, 0, 0, 0, 0.5f),
        _ => Pose.Neutral,
    };

    /// <inheritdoc />
    public void Present(CreatureRig rig, Emote emote, float speed, double delta)
    {
        ArgumentNullException.ThrowIfNull(rig);
        rig.Clock += delta;
        float t = (float)rig.Clock;
        Pose pose = rig.Current.Toward(PoseOf(emote), 1 - Mathf.Exp(-EaseRate * (float)delta));
        rig.Current = pose;

        // Body: sink, pitch, a slow breath, and a tremble when afraid.
        float breath = (Mathf.Sin(t * 1.6f) * 0.015f * rig.Height) + (emote == Emote.Fear ? Mathf.Sin(t * 47f) * 0.012f * rig.Height : 0);
        rig.Body.Position = rig.BodyRest + new Vector3(0, (-pose.BodyDrop * rig.Height) + breath, 0);
        rig.Body.Rotation = new Vector3(-pose.BodyPitch, 0, pose.HeadTilt * 0.15f);

        // Head: pitch (up = +), tilt with optional oscillation, bob.
        float tilt = pose.TiltRate > 0 ? pose.HeadTilt * Mathf.Sin(t * Mathf.Tau * pose.TiltRate) : pose.HeadTilt;
        float bob = pose.Bob * Mathf.Sin(t * Mathf.Tau * 2.2f);
        rig.Head.Rotation = new Vector3(-(pose.HeadPitch + bob), 0, tilt);

        // Tail: up (+) or tucked (−), wag.
        float wag = pose.TailWag * Mathf.Sin(t * Mathf.Tau * pose.WagRate);
        rig.Tail.Rotation = new Vector3(pose.TailPitch, wag, 0);

        // Signature (E3b: by the look's feature): ears lay flat back, wings spread, a biped's arms rise, horns dip to point
        // forward; a crest, flames or claws stand up and grow.
        float raise = pose.SignatureRaise;
        (rig.Signature.Scale, rig.Signature.Rotation) = rig.Feature switch
        {
            "ears" => (Vector3.One, new Vector3(raise * 0.9f, 0, 0)),
            "wings" => (new Vector3(1 + (raise * 0.8f), 1, 1), Vector3.Zero),
            "bones" or "none" when rig.Legs.Length == 2 => (Vector3.One, new Vector3(-raise * 1.2f, 0, 0)),
            "horns" => (Vector3.One, new Vector3(-raise * 0.5f, 0, 0)),
            _ => (new Vector3(1 + (raise * 0.5f), 1 + (raise * 2.5f), 1), Vector3.Zero),
        };

        // Legs: a trot whose stride follows ground speed; diagonal pairs move together.
        float moving = Mathf.Clamp((speed - WalkThreshold) / 2f, 0, 1);
        rig.GaitPhase += speed / (StrideMetres * pose.GaitScale) * Mathf.Tau * (float)delta;
        for (int i = 0; i < rig.Legs.Length; i++)
        {
            float phase = rig.GaitPhase + (i is 0 or 3 ? 0 : Mathf.Pi);
            rig.Legs[i].Rotation = new Vector3(Mathf.Sin(phase) * LegSwing * moving, 0, 0);
        }
    }
}
