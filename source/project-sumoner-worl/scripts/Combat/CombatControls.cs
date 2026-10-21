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
/// buttons every tick (<see cref="Buttons"/>); attacks are intents, so the server checks stamina and timing. While locked on
/// (<see cref="LockOn"/>), attacks aim at the target instead of along the camera. Spells (M2-11): C thornbolt, V entangle (it
/// lands on the lock-on target, else <see cref="BurstMetres"/> ahead), G mend (on the locked-on creature if it is one of the
/// player's, else on themselves; the server decides). The server checks mana and timing.
/// </summary>
public sealed partial class CombatControls(
    GameClient client,
    Core.Data.IDataRegistry data,
    Func<Camera3D?> camera,
    Func<(Vector3 Player, Vector3 Target)?> locked,
    Func<ulong> lockedId,
    Action<IntentKind, ulong, string, byte> send) : Node
{
    /// <summary>Where entangle lands without a lock-on target, in metres ahead.</summary>
    public const float BurstMetres = 8f;

    private const float DecimetresPerMetre = 10f;
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

    /// <summary>The last spell sent: its id, the burst distance, and when (seconds since start); null before the first.</summary>
    public (string Spell, float Distance, double At)? LastSpell { get; private set; }

    /// <summary>Casts a spell by id, aimed like an attack (also used by the self-test).</summary>
    public void Cast(string spell)
    {
        if (client.Session.State != ClientSessionState.InSession || camera() is not Camera3D view)
        {
            return;
        }

        int index = -1;
        for (int i = 0; i < data.Spells.Count; i++)
        {
            index = data.Spells[i].Id == spell ? i : index;
        }

        if (index < 0)
        {
            return;
        }

        Basis basis = view.GlobalBasis;
        var ahead = new System.Numerics.Vector3(-basis.Z.X, 0, -basis.Z.Z);
        float distance = BurstMetres;
        if (locked() is (Vector3 player, Vector3 target))
        {
            ahead = new System.Numerics.Vector3(target.X - player.X, 0, target.Z - player.Z);
            distance = new Vector2(target.X - player.X, target.Z - player.Z).Length();
        }

        ulong decimetres = (ulong)Math.Clamp(distance * DecimetresPerMetre, 0, ushort.MaxValue);
        ulong ward = spell == "mend" ? lockedId() : 0;
        ulong packed = Snapshot.EncodeFacing(ahead) | (decimetres << 8) | (ward << 24);
        send(IntentKind.Cast, packed, InteractionText.SkillName(spell), (byte)index);
        LastSpell = (spell, distance, _clock);
    }

    /// <summary>Sends an attack aimed along the camera now (also used by the self-test).</summary>
    public void Attack(bool heavy)
    {
        if (client.Session.State != ClientSessionState.InSession || camera() is not Camera3D view)
        {
            return;
        }

        Basis basis = view.GlobalBasis;
        var ahead = new System.Numerics.Vector3(-basis.Z.X, 0, -basis.Z.Z);
        if (locked() is (Vector3 player, Vector3 target))
        {
            ahead = new System.Numerics.Vector3(target.X - player.X, 0, target.Z - player.Z);
        }

        byte heading = Snapshot.EncodeFacing(ahead);
        _lastAttack = client.SendIntent(IntentKind.Attack, heading, heavy ? HeavySlot : BasicSlot);
    }

    /// <inheritdoc />
    public override void _Process(double delta) => _clock += delta;

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false } key && key.Keycode is Key.C or Key.V or Key.G)
        {
            Cast(key.Keycode switch
            {
                Key.C => "thornbolt",
                Key.V => "entangle",
                _ => "mend",
            });
            GetViewport().SetInputAsHandled();
            return;
        }

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
