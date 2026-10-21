using Godot;
using SumonerWorld.Core.Combat;
using SumonerWorld.Net.Client;
using SumonerWorld.Net.Messages;

namespace SumonerWorld.Game.Combat;

/// <summary>
/// The own character's health, stamina and mana as three bars at the bottom left (combat spec §11), from the snapshot's own
/// vitals, the combat keys above them, and a short line while down. Fractions only: exact numbers stay on the server.
/// </summary>
public sealed partial class CombatHud(GameClient client) : CanvasLayer
{
    private const float BarWidth = 220f;
    private const float BarHeight = 12f;

    private ColorRect? _health;
    private ColorRect? _stamina;
    private ColorRect? _mana;
    private Label? _down;

    /// <inheritdoc />
    public override void _Ready()
    {
        Layer = 4;
        _health = Bar(0, new Color(0.78f, 0.22f, 0.2f));
        _stamina = Bar(1, new Color(0.85f, 0.75f, 0.3f));
        _mana = Bar(2, new Color(0.3f, 0.5f, 0.9f));
        var hint = new Label { Text = "Left click: attack (hold: heavy)   Shift: block   Space: dodge" };
        hint.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        hint.Position = new Vector2(24, hint.Position.Y - 120);
        hint.AddThemeFontSizeOverride("font_size", 14);
        hint.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        hint.AddThemeConstantOverride("outline_size", 5);
        AddChild(hint);
        _down = new Label { Text = "You are down. Getting up…", Visible = false };
        _down.SetAnchorsPreset(Control.LayoutPreset.Center);
        _down.AddThemeFontSizeOverride("font_size", 26);
        _down.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        _down.AddThemeConstantOverride("outline_size", 8);
        AddChild(_down);
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        SnapshotVitals own = client.OwnVitals;
        _health!.Size = new Vector2(BarWidth * own.Health / 255f, BarHeight);
        _stamina!.Size = new Vector2(BarWidth * own.Stamina / 255f, BarHeight);
        _mana!.Size = new Vector2(BarWidth * own.Mana / 255f, BarHeight);
        _down!.Visible = own.Action == (byte)CombatAction.Down;
    }

    private ColorRect Bar(int row, Color colour)
    {
        var position = new Vector2(24, -90 + (row * (BarHeight + 6)));
        var back = new ColorRect { Color = new Color(0, 0, 0, 0.55f), Size = new Vector2(BarWidth + 4, BarHeight + 4) };
        back.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        back.Position = new Vector2(position.X - 2, back.Position.Y + position.Y - 2);
        AddChild(back);
        var fill = new ColorRect { Color = colour, Size = new Vector2(BarWidth, BarHeight) };
        fill.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        fill.Position = new Vector2(position.X, fill.Position.Y + position.Y);
        AddChild(fill);
        return fill;
    }
}
