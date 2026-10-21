using Godot;
using SumonerWorld.Core.Intents;
using SumonerWorld.Net.Client;
using SumonerWorld.Net.Messages;
using SumonerWorld.Net.Sessions;

namespace SumonerWorld.Game.Combat;

/// <summary>
/// Combat on mouse and keyboard (combat spec §3–§5): a left click attacks with the basic move, holding the left button for
/// <see cref="HeavyHoldSeconds"/> and releasing does the heavy one, both aimed where the camera looks. Shift held is the
/// guard (block, and a parry if raised just before a hit), Space dodges along the movement. Block and dodge travel as input
/// buttons every tick (<see cref="Buttons"/>); attacks are intents, so the server checks stamina and timing.
/// </summary>
public sealed partial class CombatControls(GameClient client, Func<Camera3D?> camera) : Node
{
    /// <summary>Holding the left button at least this long makes the attack heavy.</summary>
    public const double HeavyHoldSeconds = 0.4;

    private const byte BasicSlot = 0;
    private const byte HeavySlot = 1;

    private double _heldSince = -1;
    private double _clock;
    private uint _lastAttack;

    /// <summary>Block and dodge as input buttons for this tick (<see cref="PlayerButtons"/>).</summary>
    public static byte Buttons =>
        (byte)((Input.IsKeyPressed(Key.Shift) ? PlayerButtons.Block : 0) | (Input.IsKeyPressed(Key.Space) ? PlayerButtons.Dodge : 0));

    /// <summary>The result of the last attack sent, once the server answered (for the self-test log).</summary>
    public IntentResult? LastResult => _lastAttack != 0 && client.IntentResults.TryGetValue(_lastAttack, out IntentResult result) ? result : null;

    /// <summary>Sends an attack aimed along the camera now (also used by the self-test).</summary>
    public void Attack(bool heavy)
    {
        if (client.Session.State != ClientSessionState.InSession || camera() is not Camera3D view)
        {
            return;
        }

        Basis basis = view.GlobalBasis;
        var ahead = new System.Numerics.Vector3(-basis.Z.X, 0, -basis.Z.Z);
        byte heading = Snapshot.EncodeFacing(ahead);
        _lastAttack = client.SendIntent(IntentKind.Attack, heading, heavy ? HeavySlot : BasicSlot);
    }

    /// <inheritdoc />
    public override void _Process(double delta) => _clock += delta;

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
        {
            return;
        }

        if (button.Pressed)
        {
            _heldSince = _clock;
        }
        else if (_heldSince >= 0)
        {
            Attack(heavy: _clock - _heldSince >= HeavyHoldSeconds);
            _heldSince = -1;
        }

        GetViewport().SetInputAsHandled();
    }
}
