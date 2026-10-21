using Godot;
using SumonerWorld.Core.Combat;
using SumonerWorld.Game.Combat;
using SumonerWorld.Game.Creatures;
using SumonerWorld.Net.Client;

namespace SumonerWorld.Game.Characters;

/// <summary>
/// The own character on screen (M2-04, M2-11): the summoner model at the predicted position, facing where it runs (or the
/// camera's way while it fights, casts or is locked on), animated from its speed and combat action, swelling and flinching when
/// hit, wearing its status marks, and showing a spell's flight when a cast ends (a thornbolt's streak, entangle's sigil where it
/// lands).
/// </summary>
public sealed partial class OwnAvatar : Node3D
{
    /// <summary>Height of the body's centre above the feet.</summary>
    public const float BodyCentre = 0.9f;

    private const float TurnRate = 14f;
    private const float SpeedSmoothing = 8f;
    private const float BoltLength = 20f;
    private const float BurstRadius = 3f;

    private readonly GameClient _client;
    private readonly HumanoidRig _rig;
    private readonly StatusMarksView _marks = new(0.6f);
    private Vector3 _last;
    private float _yaw;
    private float _speed;
    private byte _health = byte.MaxValue;
    private byte _action;
    private double _clock;

    /// <summary>Creates the own character for a client.</summary>
    public OwnAvatar(GameClient client)
    {
        _client = client;
        Name = "OwnAvatar";
        _rig = HumanoidRig.Build(EntityViewSync.OwnRobe);
        AddChild(_rig);
        _rig.AddChild(_marks);
    }

    /// <summary>Where the feet are.</summary>
    public Vector3 Feet => _rig.GlobalPosition;

    /// <summary>Where the body's centre is (lock-on, aiming and interaction distances measure from here).</summary>
    public Vector3 Centre => _rig.GlobalPosition + new Vector3(0, BodyCentre, 0);

    /// <summary>Places and animates the character for this frame.</summary>
    /// <param name="cameraYaw">The camera's heading.</param>
    /// <param name="fighting">True while locked on (it then faces the camera's way).</param>
    /// <param name="lastSpell">The last spell sent and its burst distance, if any.</param>
    /// <param name="delta">Frame time.</param>
    public void Follow(float cameraYaw, bool fighting, (string Spell, float Distance)? lastSpell, double delta)
    {
        _clock += delta;
        System.Numerics.Vector3 p = _client.Prediction.Position;
        var feet = new Vector3(p.X, p.Y, p.Z);
        var moved = new Vector3(feet.X - _last.X, 0, feet.Z - _last.Z);

        // Prediction steps at 30 Hz while frames may come faster, so the measured speed is smoothed before it drives the gait.
        float measured = delta > 0 && moved.Length() < 2 ? moved.Length() / (float)delta : 0;
        _speed = Mathf.Lerp(_speed, measured, 1 - Mathf.Exp(-SpeedSmoothing * (float)delta));
        _last = feet;
        _rig.Position = feet;

        byte action = _client.OwnVitals.Action;
        bool busy = action != (byte)CombatAction.None || fighting;
        float wanted = !busy && moved.LengthSquared() > 1e-6f ? Mathf.Atan2(moved.X, moved.Z) : busy ? cameraYaw : _yaw;
        _yaw = Mathf.LerpAngle(_yaw, wanted, 1 - Mathf.Exp(-TurnRate * (float)delta));
        CombatPose.Apply(_rig, action, _yaw, _clock);
        HumanoidAnimator.Animate(_rig, (CombatAction)action, _speed, delta);
        if (_client.OwnVitals.Health < _health)
        {
            CombatPose.Hit(_rig);
            HumanoidAnimator.Hit(_rig);
        }

        _health = _client.OwnVitals.Health;
        CombatPose.Settle(_rig, delta);
        _marks.Show((StatusMarks)_client.OwnVitals.Status);
        ShowSpell(action, lastSpell);
    }

    // When the cast ends (not by a stagger), the spell's flight shows.
    private void ShowSpell(byte action, (string Spell, float Distance)? lastSpell)
    {
        bool ended = _action == (byte)CombatAction.Cast && action != (byte)CombatAction.Cast && action != (byte)CombatAction.Stagger;
        _action = action;
        if (!ended || lastSpell is not (string spell, float distance))
        {
            return;
        }

        var ahead = new Vector3(Mathf.Sin(_yaw), 0, Mathf.Cos(_yaw));
        if (spell == "thornbolt")
        {
            AddChild(new BoltStreak(_rig.Position, _yaw, BoltLength));
        }
        else if (spell == "entangle")
        {
            AddChild(new SigilFlash(BurstRadius) { Position = _rig.Position + (ahead * distance) });
        }
    }
}
