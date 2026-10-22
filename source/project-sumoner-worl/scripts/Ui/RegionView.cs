using Godot;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Net.Client;

namespace SumonerWorld.Game.Ui;

/// <summary>
/// The slice region on screen (M2-16): a ring of rocks around the Bogfox Den, and the place's name and line in big letters for a
/// few seconds whenever the player walks into one of the path's places.
/// </summary>
public sealed partial class RegionView(RegionDefinition region, Func<float, float, float> heightAt, Func<Vector3> player) : Node3D
{
    private const double BannerSeconds = 4;
    private static readonly Color Rock = new(0.42f, 0.4f, 0.36f);

    private Label? _banner;
    private string _inside = string.Empty;
    private double _shownFor;

    /// <summary>The banner's text now (empty when hidden; for the self-test log).</summary>
    public string Banner => _banner is { Visible: true } banner ? banner.Text : string.Empty;

    /// <inheritdoc />
    public override void _Ready()
    {
        Name = "Region";
        var rock = new StandardMaterial3D { AlbedoColor = Rock };
        foreach (PlaceDefinition place in region.Places.Where(place => place.Look == "den"))
        {
            for (int i = 0; i < 14; i++)
            {
                float angle = i * Mathf.Tau / 14;
                float x = place.X + (place.Radius * Mathf.Sin(angle));
                float z = place.Z + (place.Radius * Mathf.Cos(angle));
                float size = 0.8f + (0.5f * ((i * 7) % 3));
                var boulder = new MeshInstance3D { Mesh = new SphereMesh { Radius = size, Height = size * 1.4f, Material = rock } };
                boulder.Position = new Vector3(x, heightAt(x, z) + (size * 0.3f), z);
                AddChild(boulder);
            }
        }

        var layer = new CanvasLayer { Layer = 4 };
        _banner = new Label { Visible = false, HorizontalAlignment = HorizontalAlignment.Center };
        _banner.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _banner.GrowHorizontal = Control.GrowDirection.Both;
        _banner.Position += new Vector2(0, 120);
        _banner.AddThemeFontSizeOverride("font_size", 34);
        _banner.AddThemeColorOverride("font_color", new Color(1, 0.93f, 0.7f));
        _banner.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        _banner.AddThemeConstantOverride("outline_size", 9);
        layer.AddChild(_banner);
        AddChild(layer);
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        Vector3 at = player();
        PlaceDefinition? here = region.Places.FirstOrDefault(place => new Vector2(at.X - place.X, at.Z - place.Z).Length() <= place.Radius);
        string id = here?.Id ?? string.Empty;
        if (id != _inside && here is not null)
        {
            _banner!.Text = $"{here.Name}\n{here.Line}";
            _banner.Visible = true;
            _shownFor = 0;
        }

        _inside = id;
        _shownFor += delta;
        if (_banner!.Visible && _shownFor > BannerSeconds)
        {
            _banner.Visible = false;
        }
    }

    /// <summary>How far and which way a point lies from the player, e.g. <c>40 m north-east</c>.</summary>
    public static string Direction(Vector3 from, float x, float z) => InteractionText.Direction(x - from.X, z - from.Z);
}
