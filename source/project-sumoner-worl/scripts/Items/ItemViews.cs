using Godot;

namespace SumonerWorld.Game.Items;

/// <summary>
/// Placeholder models for things on the ground (M2-13, style guide §1–2: low poly, palette colours): the player's own loot bag (a
/// tied sack with a glint of gold, bobbing a little so it is easy to spot) and the gathering nodes (a berry bush, a sweetroot patch,
/// fallen branches). A node that was gathered loses its fruit until it regrows.
/// </summary>
public sealed partial class ItemView : Node3D
{
    private Node3D? _fruit;
    private double _clock;
    private bool _bob;

    private ItemView()
    {
    }

    /// <summary>A loot bag.</summary>
    public static ItemView Bag()
    {
        var view = new ItemView { Name = "LootBag", _bob = true };
        view.Part(new SphereMesh { Radius = 0.25f, Height = 0.45f, RadialSegments = 7, Rings = 4 }, new Color(0.55f, 0.4f, 0.25f), new Vector3(0, 0.22f, 0));
        view.Part(new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.08f, Height = 0.15f, RadialSegments = 6 }, new Color(0.4f, 0.28f, 0.17f), new Vector3(0, 0.48f, 0));
        view.Part(new CylinderMesh { TopRadius = 0.09f, BottomRadius = 0.09f, Height = 0.03f, RadialSegments = 8 }, new Color(1f, 0.82f, 0.3f), new Vector3(0.15f, 0.05f, 0.15f), glow: true);
        return view;
    }

    /// <summary>A gathering node of the kind with this id.</summary>
    public static ItemView Node(string kind)
    {
        var view = new ItemView { Name = kind };
        view._fruit = new Node3D { Name = "Fruit" };
        view.AddChild(view._fruit);
        switch (kind)
        {
            case "berry_bush":
                view.Part(new SphereMesh { Radius = 0.55f, Height = 0.9f, RadialSegments = 7, Rings = 4 }, new Color(0.22f, 0.4f, 0.2f), new Vector3(0, 0.45f, 0));
                for (int i = 0; i < 6; i++)
                {
                    float angle = i * Mathf.Tau / 6;
                    view.Part(new SphereMesh { Radius = 0.07f, Height = 0.14f, RadialSegments = 5, Rings = 3 }, new Color(0.75f, 0.15f, 0.2f),
                        new Vector3(Mathf.Sin(angle) * 0.48f, 0.5f + (Mathf.Cos(angle * 2) * 0.15f), Mathf.Cos(angle) * 0.48f), parent: view._fruit);
                }

                break;
            case "sweetroot_patch":
                for (int i = 0; i < 4; i++)
                {
                    float angle = i * Mathf.Tau / 4;
                    view.Part(new BoxMesh { Size = new Vector3(0.08f, 0.35f, 0.18f) }, new Color(0.3f, 0.5f, 0.25f),
                        new Vector3(Mathf.Sin(angle) * 0.2f, 0.17f, Mathf.Cos(angle) * 0.2f), parent: view._fruit);
                }

                view.Part(new SphereMesh { Radius = 0.12f, Height = 0.18f, RadialSegments = 6, Rings = 3 }, new Color(0.85f, 0.5f, 0.2f), new Vector3(0, 0.05f, 0), parent: view._fruit);
                view.Part(new CylinderMesh { TopRadius = 0.4f, BottomRadius = 0.45f, Height = 0.04f, RadialSegments = 8 }, new Color(0.35f, 0.27f, 0.18f), new Vector3(0, 0.02f, 0));
                break;
            default:
                for (int i = 0; i < 3; i++)
                {
                    view.Part(new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.07f, Height = 1.2f, RadialSegments = 5 }, new Color(0.42f, 0.3f, 0.2f),
                        new Vector3((i - 1) * 0.18f, 0.07f + (i * 0.04f), 0), new Vector3(Mathf.Pi / 2, i * 0.5f, 0), parent: view._fruit);
                }

                break;
        }

        return view;
    }

    /// <summary>Shows whether a node can be gathered (with its fruit) or not (bare).</summary>
    public void SetReady(bool ready)
    {
        if (_fruit is not null)
        {
            _fruit.Visible = ready;
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_bob)
        {
            _clock += delta;
            GetChild<Node3D>(0).Position = new Vector3(0, 0.22f + (Mathf.Sin((float)_clock * 3) * 0.05f), 0);
            RotateY((float)delta);
        }
    }

    private void Part(Mesh mesh, Color colour, Vector3 position, Vector3? rotation = null, Node3D? parent = null, bool glow = false)
    {
        var material = new StandardMaterial3D { AlbedoColor = colour, Roughness = 0.9f };
        if (glow)
        {
            material.EmissionEnabled = true;
            material.Emission = colour;
            material.EmissionEnergyMultiplier = 1.5f;
        }

        (parent ?? this).AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = material, Position = position, Rotation = rotation ?? Vector3.Zero });
    }
}
