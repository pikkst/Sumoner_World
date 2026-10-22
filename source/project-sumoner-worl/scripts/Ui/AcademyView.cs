using Godot;
using SumonerWorld.Core.Data.Definitions;

namespace SumonerWorld.Game.Ui;

/// <summary>
/// The Summoner Academy as the player sees it (Academy spec §1, M2-21): a ring of stone wall with a gate to the north and the
/// Grand Arena's posts. Scenery only: its people are drawn by <see cref="PeopleView"/> (E5a), what they say and do is in
/// <see cref="AcademyDialogue"/>, and the server checks everything.
/// </summary>
public sealed partial class AcademyView(AcademyDefinition academy, Func<float, float, float> heightAt) : Node3D
{
    private const int Segments = 28;
    private const float WallHeight = 2.4f;
    private static readonly Color Stone = new(0.62f, 0.6f, 0.55f);

    /// <inheritdoc />
    public override void _Ready()
    {
        Name = "Academy";
        var stone = new StandardMaterial3D { AlbedoColor = Stone };
        float step = Mathf.Tau / Segments;
        float length = academy.Radius * step * 1.05f;
        for (int i = 0; i < Segments; i++)
        {
            float angle = i * step;
            var at = new Vector2(academy.X + (academy.Radius * Mathf.Sin(angle)), academy.Z + (academy.Radius * Mathf.Cos(angle)));
            if (Mathf.Abs(Mathf.Wrap(angle + (step / 2), -Mathf.Pi, Mathf.Pi)) < step * 1.5f)
            {
                continue; // the north gate
            }

            var wall = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(length, WallHeight, 0.7f), Material = stone } };
            wall.Position = new Vector3(at.X, heightAt(at.X, at.Y) + (WallHeight / 2) - 0.3f, at.Y);
            wall.Rotation = new Vector3(0, angle, 0);
            AddChild(wall);
        }

        foreach (float side in new[] { -1f, 1f })
        {
            float x = academy.X + (side * step * 1.6f * academy.Radius);
            float z = academy.Z + academy.Radius;
            var pillar = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.6f, BottomRadius = 0.7f, Height = 4f, Material = stone } };
            pillar.Position = new Vector3(x, heightAt(x, z) + 1.8f, z);
            AddChild(pillar);
        }

        // The Grand Arena (M2-21b): a ring of low posts.
        var sand = new StandardMaterial3D { AlbedoColor = new Color(0.78f, 0.68f, 0.48f) };
        for (int i = 0; i < 16; i++)
        {
            float angle = i * Mathf.Tau / 16;
            float x = academy.ArenaX + (academy.ArenaRadius * Mathf.Sin(angle));
            float z = academy.ArenaZ + (academy.ArenaRadius * Mathf.Cos(angle));
            var post = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.18f, BottomRadius = 0.22f, Height = 0.9f, Material = sand } };
            post.Position = new Vector3(x, heightAt(x, z) + 0.4f, z);
            AddChild(post);
        }
    }
}
