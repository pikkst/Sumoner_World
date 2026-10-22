using Godot;
using SumonerWorld.Core.Data.Definitions;

namespace SumonerWorld.Game.Ui;

/// <summary>
/// The placeholder buildings of the region's places (M2-16, E4b), from primitives in the style guide's greybox colours: a den is a
/// ring of rocks; a town a ring of houses round a well; an outpost a palisade with a gap to the south, two huts and a banner in its
/// colour; ruins a ring of broken white pillars and fallen blocks; a camp grey tents round a fire. Real models replace them.
/// </summary>
public static class PlaceLooks
{
    private static readonly Color Rock = new(0.42f, 0.4f, 0.36f);
    private static readonly Color Wood = new(0.45f, 0.32f, 0.2f);
    private static readonly Color Plaster = new(0.82f, 0.77f, 0.66f);
    private static readonly Color Roof = new(0.55f, 0.22f, 0.16f);
    private static readonly Color WhiteStone = new(0.86f, 0.85f, 0.8f);
    private static readonly Color Canvas = new(0.55f, 0.55f, 0.52f);

    /// <summary>Builds the look of <paramref name="place"/> under <paramref name="parent"/> (nothing for <c>none</c>).</summary>
    public static void Build(Node3D parent, PlaceDefinition place, Func<float, float, float> heightAt)
    {
        ArgumentNullException.ThrowIfNull(place);
        var look = new Node3D { Name = $"Look-{place.Id}" };
        parent.AddChild(look);
        var at = new Placer(look, place, heightAt);
        switch (place.Look)
        {
            case "den":
                Den(at);
                break;
            case "town":
                Town(at);
                break;
            case "outpost":
                Outpost(at, place.Colour.Length > 0 ? Color.FromHtml(place.Colour) : Roof);
                break;
            case "ruins":
                Ruins(at);
                break;
            case "camp":
                Camp(at);
                break;
        }
    }

    private static void Den(Placer at)
    {
        StandardMaterial3D rock = Material(Rock);
        for (int i = 0; i < 14; i++)
        {
            float size = 0.8f + (0.5f * ((i * 7) % 3));
            at.Ring(i, 14, 1f, new SphereMesh { Radius = size, Height = size * 1.4f, Material = rock }, size * 0.3f);
        }
    }

    private static void Town(Placer at)
    {
        StandardMaterial3D walls = Material(Plaster);
        StandardMaterial3D roof = Material(Roof);
        for (int i = 0; i < 7; i++)
        {
            MeshInstance3D house = at.Ring(i, 7, 0.65f, new BoxMesh { Size = new Vector3(5, 3.2f, 4), Material = walls }, 1.6f);
            house.AddChild(new MeshInstance3D { Mesh = new PrismMesh { Size = new Vector3(5.6f, 2, 4.4f), Material = roof }, Position = new Vector3(0, 2.6f, 0) });
        }

        at.Centre(new CylinderMesh { TopRadius = 1.1f, BottomRadius = 1.2f, Height = 1f, Material = Material(Rock) }, 0.5f);
        at.Centre(new BoxMesh { Size = new Vector3(0.2f, 2.6f, 0.2f), Material = Material(Wood) }, 1.3f, new Vector3(2.5f, 0, 0));
    }

    private static void Outpost(Placer at, Color banner)
    {
        StandardMaterial3D log = Material(Wood);
        const int posts = 36;
        for (int i = 0; i < posts; i++)
        {
            // A gap facing south (the way into the Vale).
            if (i is >= 16 and <= 19)
            {
                continue;
            }

            at.Ring(i, posts, 1f, new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.3f, Height = 3.5f, Material = log }, 1.75f);
        }

        StandardMaterial3D walls = Material(Wood.Lightened(0.2f));
        for (int i = 0; i < 2; i++)
        {
            MeshInstance3D hut = at.Ring(i, 2, 0.45f, new BoxMesh { Size = new Vector3(4, 2.6f, 3.5f), Material = walls }, 1.3f, offset: 0.4f);
            hut.AddChild(new MeshInstance3D { Mesh = new PrismMesh { Size = new Vector3(4.5f, 1.6f, 4), Material = Material(banner.Darkened(0.3f)) }, Position = new Vector3(0, 2.1f, 0) });
        }

        at.Centre(new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.1f, Height = 7, Material = log }, 3.5f);
        at.Centre(new BoxMesh { Size = new Vector3(0.05f, 1.4f, 2.2f), Material = Material(banner, glow: true) }, 6.2f, new Vector3(0, 0, 1.1f));
    }

    private static void Ruins(Placer at)
    {
        StandardMaterial3D stone = Material(WhiteStone);
        for (int i = 0; i < 12; i++)
        {
            float height = 1.5f + (((i * 5) % 4) * 1.3f);
            at.Ring(i, 12, 1f, new CylinderMesh { TopRadius = 0.7f, BottomRadius = 0.8f, Height = height, RadialSegments = 8, Material = stone }, height / 2);
            if (i % 3 == 1)
            {
                MeshInstance3D block = at.Ring(i, 12, 0.8f, new BoxMesh { Size = new Vector3(2.2f, 0.9f, 1.2f), Material = stone }, 0.4f, offset: 0.2f);
                block.Rotation = new Vector3(0.2f, block.Rotation.Y + 0.7f, 0);
            }
        }
    }

    private static void Camp(Placer at)
    {
        StandardMaterial3D canvas = Material(Canvas);
        for (int i = 0; i < 5; i++)
        {
            at.Ring(i, 5, 0.6f, new CylinderMesh { TopRadius = 0, BottomRadius = 2.2f, Height = 3, RadialSegments = 6, Material = canvas }, 1.5f);
        }

        at.Centre(new CylinderMesh { TopRadius = 0, BottomRadius = 0.6f, Height = 1.2f, RadialSegments = 5, Material = Material(new Color(1f, 0.5f, 0.15f), glow: true) }, 0.6f);
        at.Centre(new BoxMesh { Size = new Vector3(1, 1, 1), Material = Material(Wood) }, 0.5f, new Vector3(3, 0, 1));
        at.Centre(new BoxMesh { Size = new Vector3(1, 1, 1), Material = Material(Wood) }, 0.5f, new Vector3(3.4f, 0, -0.4f));
    }

    private static StandardMaterial3D Material(Color colour, bool glow = false) => new()
    {
        AlbedoColor = colour,
        Roughness = 0.9f,
        EmissionEnabled = glow,
        Emission = colour,
        EmissionEnergyMultiplier = glow ? 0.6f : 0,
    };

    // Puts parts on the ground around a place, facing its centre.
    private sealed class Placer(Node3D parent, PlaceDefinition place, Func<float, float, float> heightAt)
    {
        public MeshInstance3D Ring(int index, int count, float share, Mesh mesh, float lift, float offset = 0)
        {
            float angle = (index + offset) * Mathf.Tau / count;
            float x = place.X + (place.Radius * share * Mathf.Sin(angle));
            float z = place.Z + (place.Radius * share * Mathf.Cos(angle));
            var part = new MeshInstance3D { Mesh = mesh, Position = new Vector3(x, heightAt(x, z) + lift, z), Rotation = new Vector3(0, angle, 0) };
            parent.AddChild(part);
            return part;
        }

        public void Centre(Mesh mesh, float lift, Vector3 offset = default)
        {
            float x = place.X + offset.X;
            float z = place.Z + offset.Z;
            parent.AddChild(new MeshInstance3D { Mesh = mesh, Position = new Vector3(x, heightAt(x, z) + lift, z) });
        }
    }
}
