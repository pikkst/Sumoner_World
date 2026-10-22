using Godot;
using SumonerWorld.Core.Data;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Game.Characters;

namespace SumonerWorld.Game.Ui;

/// <summary>
/// Every person on screen (Academy spec §1, E5a): the Academy's people and the Vale's, each in their robe with their name above
/// their head, standing where the data puts them or walking their round. Where a walker is comes from the world tick
/// (<see cref="Core.World.People.At"/>), which this view keeps in step with the server's snapshots, so client and server agree on
/// who is in talking range. What they say is in <see cref="AcademyDialogue"/>.
/// </summary>
public sealed partial class PeopleView(IDataRegistry data, Func<float, float, float> heightAt, Func<ulong> serverTick) : Node3D
{
    private const double TicksPerSecond = 30;
    private const double ResyncTicks = 30;

    private readonly List<HumanoidRig> _people = [];
    private double _tick;

    /// <summary>The people, in <see cref="IDataRegistry.People"/> order.</summary>
    public IReadOnlyList<HumanoidRig> People => _people;

    /// <summary>The world tick this view shows (smoothed between snapshots).</summary>
    public double Tick => _tick;

    /// <inheritdoc />
    public override void _Ready()
    {
        Name = "People";
        foreach (NpcDefinition person in data.People)
        {
            HumanoidRig rig = HumanoidRig.Build(Color.FromHtml(person.Robe));
            rig.AddChild(new Label3D
            {
                Text = person.Name,
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

        Place(0);
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        // Follow the server's clock: run on between snapshots, jump when far off (a reconnect, the first snapshot).
        ulong server = serverTick();
        _tick += delta * TicksPerSecond;
        if (server > 0 && Math.Abs(_tick - server) > ResyncTicks)
        {
            _tick = server;
        }

        Place(delta);
    }

    /// <summary>The index of the person within talking range of <paramref name="at"/>, or −1.</summary>
    public int PersonNear(Vector3 at) => Core.World.People.Near(data, at.X, at.Z, _tick, Core.Academy.TalkTransaction.Range - 0.2f);

    private void Place(double delta)
    {
        for (int i = 0; i < _people.Count; i++)
        {
            HumanoidRig rig = _people[i];
            (float x, float z, float facing) = Core.World.People.At(data.People[i], _tick);
            var position = new Vector3(x, heightAt(x, z), z);
            float speed = delta > 0 ? new Vector2(position.X - rig.Position.X, position.Z - rig.Position.Z).Length() / (float)delta : 0;
            rig.Position = position;
            rig.Rotation = new Vector3(0, Mathf.DegToRad(facing), 0);
            HumanoidAnimator.Animate(rig, Core.Combat.CombatAction.None, Mathf.Min(speed, 4f), delta);
        }
    }
}
