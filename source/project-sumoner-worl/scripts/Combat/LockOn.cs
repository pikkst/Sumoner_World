using Godot;
using SumonerWorld.Game.Creatures;
using SumonerWorld.Net.Client;
using SumonerWorld.Net.Messages;

namespace SumonerWorld.Game.Combat;

/// <summary>
/// Lock-on (combat spec §11a, stakeholder 2026-10-21): the middle mouse button or T locks the camera on the nearest creature in
/// front of it within <see cref="RangeMetres"/>; pressing again releases it, and so does the target leaving view, going farther
/// than the range or being defeated. While locked, the camera keeps the target in view (<see cref="Hosting.OrbitCamera"/>), WASD circles
/// it, and attacks aim at it (<see cref="CombatControls"/>). Only where the server allows it (<see cref="GameClient.LockOnAllowed"/>:
/// Normal servers, not Hardcore). A red marker floats over the target.
/// </summary>
public sealed partial class LockOn(GameClient client, EntityViewSync views, Func<Camera3D?> camera, Func<Vector3> player) : Node
{
    /// <summary>Farthest lock, in metres.</summary>
    public const float RangeMetres = 25f;

    private const float MinFacing = 0.3f;
    private const float MarkerHeight = 3.0f;

    private Label3D? _marker;

    /// <summary>The locked creature's stable id; 0 when not locked.</summary>
    public ulong Target { get; private set; }

    /// <summary>Where the locked target is now (its view), if locked.</summary>
    public bool TryGetTarget(out Vector3 position)
    {
        position = default;
        if (Target == 0 || !views.TryGetCreature(Target, out Node3D? view) || view is null)
        {
            return false;
        }

        position = view.GlobalPosition;
        return true;
    }

    /// <summary>Locks on to the best target, or releases the lock (also used by the self-test).</summary>
    public void Toggle()
    {
        Target = Target != 0 || !client.LockOnAllowed ? 0 : Pick();
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        _marker = new Label3D
        {
            Text = "▼",
            FontSize = 64,
            Modulate = new Color(1f, 0.3f, 0.25f),
            OutlineSize = 12,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Visible = false,
        };
        AddChild(_marker);
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        bool pressed = @event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Middle }
            || @event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.T };
        if (pressed)
        {
            Toggle();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        bool locked = TryGetTarget(out Vector3 target) && target.DistanceTo(player()) <= RangeMetres && Alive();
        if (!locked)
        {
            Target = 0;
        }

        _marker!.Visible = locked;
        if (locked)
        {
            _marker.GlobalPosition = target + new Vector3(0, MarkerHeight, 0);
        }
    }

    private bool Alive() =>
        !client.Entities.TryGetValue((SnapshotEntityKind.Creature, Target), out SnapshotEntity entity) || entity.Health > 0;

    // The nearest creature in range that is in front of the camera.
    private ulong Pick()
    {
        if (camera() is not Camera3D view)
        {
            return 0;
        }

        Vector3 from = player();
        Vector3 ahead = -view.GlobalBasis.Z with { Y = 0 };
        ahead = ahead.Normalized();
        (ulong id, float distance) best = (0, RangeMetres);
        foreach ((ulong id, Node3D creature) in views.Creatures)
        {
            Vector3 offset = creature.GlobalPosition - from;
            offset.Y = 0;
            float distance = offset.Length();
            if (distance < best.distance && distance > 0 && offset.Normalized().Dot(ahead) >= MinFacing)
            {
                best = (id, distance);
            }
        }

        return best.id;
    }
}
