using Godot;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Game.Characters;

namespace SumonerWorld.Game.Ui;

/// <summary>
/// The Summoner Academy as the player sees it (Academy spec §1, M2-21): a ring of stone wall with a gate to the north and its
/// people standing where the data puts them, each with a name above their head. Scenery only: what the people say and do is in
/// <see cref="AcademyDialogue"/>, and the server checks everything.
/// </summary>
public sealed partial class AcademyView(AcademyDefinition academy, Func<float, float, float> heightAt) : Node3D
{
    private const int Segments = 28;
    private const float WallHeight = 2.4f;
    private static readonly Color Stone = new(0.62f, 0.6f, 0.55f);

    private readonly List<HumanoidRig> _people = [];

    /// <summary>The people, in data order.</summary>
    public IReadOnlyList<HumanoidRig> People => _people;

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

        foreach (NpcDefinition npc in academy.Npcs)
        {
            AddPerson(npc);
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        foreach (HumanoidRig rig in _people)
        {
            HumanoidAnimator.Animate(rig, Core.Combat.CombatAction.None, 0, delta);
        }
    }

    private void AddPerson(NpcDefinition npc)
    {
        HumanoidRig rig = HumanoidRig.Build(Color.FromHtml(npc.Robe));
        rig.Position = new Vector3(npc.X, heightAt(npc.X, npc.Z), npc.Z);
        rig.Rotation = new Vector3(0, Mathf.DegToRad(npc.Facing), 0);
        rig.AddChild(new Label3D
        {
            Text = npc.Name,
            Position = new Vector3(0, 2.5f, 0),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 44,
            PixelSize = 0.006f,
            OutlineSize = 10,
            Modulate = new Color(1, 0.92f, 0.6f),
        });
        _people.Add(rig);
        AddChild(rig);
    }

    /// <summary>The index of the person within talking range of <paramref name="at"/>, or −1.</summary>
    public int PersonNear(Vector3 at)
    {
        int best = -1;
        float nearest = Core.Academy.TalkTransaction.Range - 0.2f;
        for (int i = 0; i < academy.Npcs.Count; i++)
        {
            float distance = new Vector2(at.X - academy.Npcs[i].X, at.Z - academy.Npcs[i].Z).Length();
            if (distance < nearest)
            {
                nearest = distance;
                best = i;
            }
        }

        return best;
    }
}
