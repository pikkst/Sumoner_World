using Godot;
using SumonerWorld.Core.Combat;
using SumonerWorld.Net.Client;
using SumonerWorld.Net.Messages;

namespace SumonerWorld.Game.Combat;

/// <summary>
/// The own character's health, stamina and mana as three bars at the bottom left (combat spec §11), from the snapshot's own
/// vitals, the combat keys above them, and a short line while down; below them class, rank and level with a thin experience bar
/// (M2-09). Fractions only: exact numbers stay on the server.
/// </summary>
public sealed partial class CombatHud(GameClient client) : CanvasLayer
{
    private const float BarWidth = 220f;
    private const float BarHeight = 12f;

    private ColorRect? _health;
    private ColorRect? _stamina;
    private ColorRect? _mana;
    private const string Keys = "Left click: attack (hold: heavy)   Shift: block   Space: dodge";

    private Label? _down;
    private Label? _hint;
    private Label? _player;
    private ColorRect? _xp;

    /// <inheritdoc />
    public override void _Ready()
    {
        Layer = 4;
        _health = Bar(0, new Color(0.78f, 0.22f, 0.2f));
        _stamina = Bar(1, new Color(0.85f, 0.75f, 0.3f));
        _mana = Bar(2, new Color(0.3f, 0.5f, 0.9f));
        _xp = new ColorRect { Color = new Color(0.85f, 0.8f, 0.55f), Size = new Vector2(BarWidth, 4) };
        _xp.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        _xp.Position = new Vector2(24, _xp.Position.Y - 34);
        AddChild(_xp);
        _player = new Label();
        _player.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        _player.Position = new Vector2(24, _player.Position.Y - 30);
        _player.AddThemeFontSizeOverride("font_size", 15);
        _player.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        _player.AddThemeConstantOverride("outline_size", 5);
        AddChild(_player);
        _hint = new Label { Text = Keys };
        Label hint = _hint;
        hint.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        hint.Position = new Vector2(24, hint.Position.Y - 140);
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
        _player!.Text = $"{InteractionText.PlayerLine((Core.Progression.SummonerClass)own.Class, (Core.Progression.Rank)own.Rank, own.Level)} · {client.Inventory.Gold} gold";
        _xp!.Size = new Vector2(BarWidth * own.Xp / 255f, 4);
        _hint!.Text = (client.LockOnAllowed ? Keys + "   Middle click / T: lock on" : Keys) + "\n" + InteractionText.SpellHints;
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
