using Godot;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Progression;
using SumonerWorld.Core.World;

namespace SumonerWorld.Game.Creatures;

/// <summary>
/// Readability check for life stages (M2-09, <c>--showcase=stages</c>): the grazer and the predator side-on as young, grown and
/// elder, seen from <see cref="Distance"/> metres. Labels are for the reviewer only.
/// </summary>
public sealed partial class StageShowcase : Node3D
{
    private const float Spacing = 5.5f;

    private static readonly LifeStage[] Stages = [LifeStage.Young, LifeStage.Grown, LifeStage.Elder];
    private static readonly CreatureRole[] Roles = [CreatureRole.Predator, CreatureRole.Grazer];

    private readonly Heightfield _ground;
    private readonly Camera3D _camera;
    private readonly QuadrupedEmotePresenter _presenter = new();
    private readonly List<CreatureRig> _rigs = [];

    /// <summary>Creates the showcase.</summary>
    public StageShowcase(Heightfield ground, Camera3D camera, float distance)
    {
        _ground = ground;
        _camera = camera;
        Distance = distance;
        Name = "StageShowcase";
    }

    /// <summary>Camera distance to the rows.</summary>
    public float Distance { get; }

    /// <inheritdoc />
    public override void _Ready()
    {
        var centre = new Vector3(_ground.Size / 2, 0, _ground.Size / 2);
        for (int row = 0; row < Roles.Length; row++)
        {
            for (int i = 0; i < Stages.Length; i++)
            {
                // Left to right as seen from the camera, which looks along +Z (screen right is −X).
                float x = centre.X + Spacing - (i * Spacing);
                float z = centre.Z + (row * Spacing);
                CreatureRig rig = CreatureRig.Build(Roles[row]);
                rig.SetStage(Stages[i]);
                rig.Position = new Vector3(x, _ground.HeightAt(x, z), z);
                rig.Rotation = new Vector3(0, Mathf.Pi / 2, 0);
                rig.Scale = Vector3.One * rig.Size;
                AddChild(rig);
                _rigs.Add(rig);
                AddChild(new Label3D
                {
                    Text = $"{Roles[row]} {Stages[i]}".ToLowerInvariant(),
                    Position = rig.Position + new Vector3(0, (rig.Height * 2.2f) + 0.3f, 0),
                    FontSize = 64,
                    PixelSize = 0.008f,
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                    OutlineSize = 10,
                });
            }
        }

        float ground = _ground.HeightAt(centre.X, centre.Z);
        _camera.Position = new Vector3(centre.X, Mathf.Max(_ground.HeightAt(centre.X, centre.Z - Distance), ground) + 2.5f, centre.Z - Distance);
        _camera.LookAt(centre + new Vector3(0, ground + 1, Spacing / 2), Vector3.Up);
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        foreach (CreatureRig rig in _rigs)
        {
            _presenter.Present(rig, Emote.None, 0, delta);
        }
    }
}
