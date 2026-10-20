using Godot;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.World;

namespace SumonerWorld.Game.Creatures;

/// <summary>
/// Readability check for the style guide §4 contract ("each emote readable at 30 m without UI"): one creature per emote (and one
/// idle) stands side-on in a row on the test region, seen from <see cref="Distance"/> metres at eye height. Labels sit above them
/// for the reviewer only. Every second creature walks on the spot so the gait is visible too.
/// </summary>
public sealed partial class EmoteShowcase : Node3D
{
    private const float Spacing = 5f;

    private static readonly Emote[] Shown = [Emote.None, Emote.Trust, Emote.Fear, Emote.Grudge, Emote.Curiosity, Emote.Affection, Emote.Grief];

    private readonly CreatureRole _role;
    private readonly Heightfield _ground;
    private readonly Camera3D _camera;
    private readonly QuadrupedEmotePresenter _presenter = new();
    private readonly List<CreatureRig> _rigs = [];

    /// <summary>Creates the showcase for one model.</summary>
    public EmoteShowcase(CreatureRole role, Heightfield ground, Camera3D camera, float distance)
    {
        _role = role;
        _ground = ground;
        _camera = camera;
        Distance = distance;
        Name = "EmoteShowcase";
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
            CreatureRig rig = CreatureRig.Build(_role);
            rig.Position = new Vector3(x, _ground.HeightAt(x, centre.Z), centre.Z);
            rig.Rotation = new Vector3(0, Mathf.Pi / 2, 0);
            rig.Clock = i * 0.37;
            AddChild(rig);
            _rigs.Add(rig);
            AddChild(new Label3D
            {
                Text = Shown[i] == Emote.None ? "idle" : Shown[i].ToString(),
                Position = rig.Position + new Vector3(0, rig.Height * 2.4f, 0),
                FontSize = 96,
                PixelSize = 0.01f,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                Modulate = new Color(1, 1, 1),
                OutlineSize = 12,
            });
        }

        float eye = _ground.HeightAt(centre.X, centre.Z - Distance) + 1.7f;
        _camera.Position = new Vector3(centre.X, Mathf.Max(eye, _ground.HeightAt(centre.X, centre.Z) + 1.7f), centre.Z - Distance);
        _camera.LookAt(centre + new Vector3(0, _ground.HeightAt(centre.X, centre.Z) + 1, 0), Vector3.Up);
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        for (int i = 0; i < _rigs.Count; i++)
        {
            _presenter.Present(_rigs[i], Shown[i], i % 2 == 1 ? 1.5f : 0f, delta);
        }
    }
}
