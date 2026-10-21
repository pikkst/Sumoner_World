using Godot;
using SumonerWorld.Core.Combat;
using SumonerWorld.Core.World;
using SumonerWorld.Game.Combat;

namespace SumonerWorld.Game.Characters;

/// <summary>
/// Readability check for the player character (M2-04, <c>--showcase=player</c>): one summoner per animation stands side-on in a row
/// (idle, walk, run, each combat action, and one that is hit every second), seen from <see cref="Distance"/> metres. Labels are for
/// the reviewer only.
/// </summary>
public sealed partial class HumanoidShowcase : Node3D
{
    private const float Spacing = 2.2f;
    private const double HitEverySeconds = 1;

    private static readonly (string Label, CombatAction Action, float Speed)[] Shown =
    [
        ("idle", CombatAction.None, 0),
        ("walk", CombatAction.None, 1.5f),
        ("run", CombatAction.None, 6),
        ("wind-up", CombatAction.Windup, 0),
        ("strike", CombatAction.Strike, 0),
        ("block", CombatAction.Block, 0),
        ("dodge", CombatAction.Dodge, 0),
        ("hit", CombatAction.None, 0),
        ("stagger", CombatAction.Stagger, 0),
        ("yield", CombatAction.Yield, 0),
        ("down", CombatAction.Down, 0),
    ];

    private readonly Heightfield _ground;
    private readonly Camera3D _camera;
    private readonly List<HumanoidRig> _rigs = [];
    private double _time;
    private double _nextHit;

    /// <summary>Creates the showcase.</summary>
    public HumanoidShowcase(Heightfield ground, Camera3D camera, float distance)
    {
        _ground = ground;
        _camera = camera;
        Distance = distance;
        Name = "HumanoidShowcase";
    }

    /// <summary>Camera distance to the row.</summary>
    public float Distance { get; }

    /// <inheritdoc />
    public override void _Ready()
    {
        var centre = new Vector3(_ground.Size / 2, 0, _ground.Size / 2);
        float width = (Shown.Length - 1) * Spacing;
        for (int i = 0; i < Shown.Length; i++)
        {
            // Left to right as seen from the camera, which looks along +Z (screen right is −X).
            float x = centre.X + (width / 2) - (i * Spacing);
            HumanoidRig rig = HumanoidRig.Build(i % 2 == 0 ? Creatures.EntityViewSync.OwnRobe : Creatures.EntityViewSync.OtherRobe);
            rig.Position = new Vector3(x, _ground.HeightAt(x, centre.Z), centre.Z);
            rig.Clock = i * 0.37;
            AddChild(rig);
            _rigs.Add(rig);
            AddChild(new Label3D
            {
                Text = Shown[i].Label,
                Position = rig.Position + new Vector3(0, 2.3f, 0),
                FontSize = 64,
                PixelSize = 0.006f,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                OutlineSize = 10,
            });
        }

        float ground = _ground.HeightAt(centre.X, centre.Z);
        _camera.Position = new Vector3(centre.X, Mathf.Max(_ground.HeightAt(centre.X, centre.Z - Distance), ground) + 1.6f, centre.Z - Distance);
        _camera.LookAt(centre + new Vector3(0, ground + 0.9f, 0), Vector3.Up);
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        _time += delta;
        bool hit = _time >= _nextHit;
        if (hit)
        {
            _nextHit = _time + HitEverySeconds;
        }

        for (int i = 0; i < _rigs.Count; i++)
        {
            (string label, CombatAction action, float speed) = Shown[i];
            HumanoidRig rig = _rigs[i];
            CombatPose.Apply(rig, (byte)action, Mathf.Pi / 2, _time);
            HumanoidAnimator.Animate(rig, action, speed, delta);
            if (hit && label == "hit")
            {
                HumanoidAnimator.Hit(rig);
            }
        }
    }
}
