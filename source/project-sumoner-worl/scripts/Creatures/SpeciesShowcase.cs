using Godot;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Data;
using SumonerWorld.Core.World;

namespace SumonerWorld.Game.Creatures;

/// <summary>
/// Readability check for the species' looks (E3b, <c>--showcase=species</c>): every species in the game data, grown, side-on in
/// rows of <see cref="PerRow"/>, seen from the showcase distance. Labels are for the reviewer only.
/// </summary>
public sealed partial class SpeciesShowcase(IDataRegistry data, Heightfield ground, Camera3D camera, float distance) : Node3D
{
    private const int PerRow = 5;
    private const float Spacing = 4.5f;

    private readonly QuadrupedEmotePresenter _presenter = new();
    private readonly List<CreatureRig> _rigs = [];

    /// <inheritdoc />
    public override void _Ready()
    {
        Name = "SpeciesShowcase";
        var centre = new Vector3(ground.Size / 2, 0, ground.Size / 2);
        for (int i = 0; i < data.Species.Count; i++)
        {
            // Left to right as seen from the camera, which looks along +Z (screen right is −X).
            float x = centre.X + (Spacing * 2) - (i % PerRow * Spacing);
            float z = centre.Z + (i / PerRow * Spacing);
            CreatureRig rig = CreatureRig.Build(data.Species[i]);
            rig.Position = new Vector3(x, ground.HeightAt(x, z), z);
            rig.Rotation = new Vector3(0, Mathf.Pi / 2, 0);
            rig.Scale = Vector3.One * rig.Size;
            AddChild(rig);
            _rigs.Add(rig);
            AddChild(new Label3D
            {
                Text = $"{data.Species[i].Id} ({data.Species[i].Class})".ToLowerInvariant(),
                Position = rig.Position + new Vector3(0, (rig.Height * rig.Size * 2) + 0.4f, 0),
                FontSize = 48,
                PixelSize = 0.008f,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                OutlineSize = 10,
            });
        }

        float height = ground.HeightAt(centre.X, centre.Z);
        camera.Position = new Vector3(centre.X, Mathf.Max(ground.HeightAt(centre.X, centre.Z - distance), height) + 6f, centre.Z - distance);
        camera.LookAt(centre + new Vector3(0, height + 1, Spacing), Vector3.Up);
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
