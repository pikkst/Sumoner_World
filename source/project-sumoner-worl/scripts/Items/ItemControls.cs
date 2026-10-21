using Godot;
using SumonerWorld.Core.Data;
using SumonerWorld.Game.Playtest;
using SumonerWorld.Net.Client;
using SumonerWorld.Net.Messages;

namespace SumonerWorld.Game.Items;

/// <summary>
/// Loot, gathering and the inventory on the keyboard (crafting spec §2–5, M2-13). The player's own loot bags are picked up by
/// walking onto them (within <see cref="PickUpMetres"/>); H gathers from the nearest node that has grown back (within
/// <see cref="GatherMetres"/>, a prompt says what). The bag itself is in the <see cref="Ui.CharacterWindow"/> (I, M2-14b). The
/// server checks everything; the result lines come through the <see cref="InteractionControls"/>.
/// </summary>
public sealed partial class ItemControls(GameClient client, IDataRegistry data, InteractionControls controls, Func<Vector3> player) : CanvasLayer
{
    /// <summary>How close a bag must be to be picked up.</summary>
    public const float PickUpMetres = 2f;

    /// <summary>How close a node must be to gather from it.</summary>
    public const float GatherMetres = 2.8f;

    private const double RetrySeconds = 1;

    private readonly Dictionary<ulong, double> _asked = [];
    private Label? _prompt;
    private double _clock;
    private (ulong Id, string Item) _node;

    /// <summary>Gathers from the node in reach, if any (also used by the self-test); false when there is none.</summary>
    public bool Gather()
    {
        if (_node.Id == 0)
        {
            return false;
        }

        controls.Send(IntentKind.Gather, _node.Id, InteractionText.SkillName(_node.Item).ToLowerInvariant(), 0);
        return true;
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        Layer = 5;
        _prompt = new Label { HorizontalAlignment = HorizontalAlignment.Center, GrowHorizontal = Control.GrowDirection.Both };
        _prompt.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _prompt.Position += new Vector2(0, -150);
        Style(_prompt, 18);
        AddChild(_prompt);
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key || controls.Menu.IsOpen)
        {
            return;
        }

        if (key.Keycode == Key.H)
        {
            Gather();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        _clock += delta;
        Vector3 me = player();
        float nearest = GatherMetres;
        _node = (0, string.Empty);
        foreach (((SnapshotEntityKind kind, ulong id), SnapshotEntity entity) in client.Entities)
        {
            float distance = new Vector2(entity.Position.X - me.X, entity.Position.Z - me.Z).Length();
            if (kind == SnapshotEntityKind.Loot && distance <= PickUpMetres && (!_asked.TryGetValue(id, out double at) || _clock - at >= RetrySeconds))
            {
                _asked[id] = _clock;
                controls.Send(IntentKind.PickUp, id, "loot", 0);
            }
            else if (kind == SnapshotEntityKind.Node && entity.Health > 0 && distance < nearest && entity.Species < data.GatherNodes.Count)
            {
                nearest = distance;
                _node = (id, data.GatherNodes[entity.Species].Item);
            }
        }

        _prompt!.Text = _node.Id == 0 ? string.Empty : InteractionText.GatherHint(NodeKind());
    }

    private string NodeKind()
    {
        foreach (Core.Data.Definitions.GatherNodeDefinition node in data.GatherNodes)
        {
            if (node.Item == _node.Item)
            {
                return node.Id;
            }
        }

        return string.Empty;
    }

    private static void Style(Label label, int size)
    {
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        label.AddThemeConstantOverride("outline_size", 6);
    }
}
