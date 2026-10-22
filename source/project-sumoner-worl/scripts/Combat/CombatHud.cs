using Godot;
using SumonerWorld.Core.Combat;
using SumonerWorld.Net.Client;
using SumonerWorld.Net.Messages;

namespace SumonerWorld.Game.Combat;

/// <summary>
/// The own character at the bottom left (combat spec §11, M2-17): health, stamina and mana as three bars with their names on them,
/// from the snapshot's own vitals; a warning in plain words when one is low (the health bar also blinks); the combat keys above
/// and "F1: keys, Esc: settings" below while the key hints are on; class, rank, level and gold with a thin experience bar (M2-09);
/// and a short line in the middle while down. Fractions only: exact numbers stay on the server.
/// </summary>
public sealed partial class CombatHud(GameClient client, Ui.SettingsWindow settings) : CanvasLayer
{
    private const float BarWidth = 230f;
    private const float BarHeight = 20f;

    private ColorRect? _health;
    private ColorRect? _stamina;
    private ColorRect? _mana;
    private ColorRect? _xp;
    private Label? _down;
    private Label? _keys;
    private Label? _warning;
    private Label? _player;
    private Label? _hint;
    private double _clock;

    /// <summary>The warning shown now (empty when none; for the self-test log).</summary>
    public string Warning => _warning is { Visible: true } warning ? warning.Text : string.Empty;

    /// <inheritdoc />
    public override void _Ready()
    {
        Layer = 4;
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 4);
        column.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        column.GrowVertical = Control.GrowDirection.Begin;
        column.OffsetLeft = 20;
        column.OffsetBottom = -Ui.UiSlot.SlotSize - 22; // above the action bar, which spreads under it at bigger text sizes
        AddChild(column);
        _keys = Add(column, Text(15));
        _warning = Add(column, Text(18));
        _warning.AddThemeColorOverride("font_color", new Color(1, 0.6f, 0.35f));
        _health = Bar(column, "Health", new Color(0.8f, 0.22f, 0.2f));
        _stamina = Bar(column, "Stamina", new Color(0.82f, 0.7f, 0.25f));
        _mana = Bar(column, "Mana", new Color(0.3f, 0.5f, 0.92f));
        _player = Add(column, Text(16));
        var xpBack = new ColorRect { Color = new Color(0, 0, 0, 0.55f), CustomMinimumSize = new Vector2(BarWidth, 5), SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        _xp = new ColorRect { Color = new Color(0.85f, 0.8f, 0.55f), Size = new Vector2(0, 5) };
        xpBack.AddChild(_xp);
        column.AddChild(xpBack);
        _hint = Add(column, Text(14));
        _hint.Text = HudText.BarHint;
        _down = Text(28);
        _down.Text = "You are down. Getting up…";
        _down.Visible = false;
        _down.SetAnchorsPreset(Control.LayoutPreset.Center);
        AddChild(_down);
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        _clock += delta;
        SnapshotVitals own = client.OwnVitals;
        bool down = own.Action == (byte)CombatAction.Down;
        Fill(_health!, own.Health);
        Fill(_stamina!, own.Stamina);
        Fill(_mana!, own.Mana);
        _health!.Modulate = own.Health < HudText.LowShare && !down ? new Color(1, 1, 1, 0.55f + (0.45f * Mathf.Abs(Mathf.Sin((float)_clock * 4)))) : Colors.White;
        _xp!.Size = new Vector2(BarWidth * own.Xp / 255f, 5);
        _down!.Visible = down;
        _warning!.Text = HudText.Warning(own.Health, own.Stamina, own.Mana, down);
        _warning.Visible = _warning.Text.Length > 0;
        _player!.Text = $"{InteractionText.PlayerLine((Core.Progression.SummonerClass)own.Class, (Core.Progression.Rank)own.Rank, own.Level)} · {client.Inventory.Gold} gold";
        bool hints = settings.Settings.ShowHints;
        _keys!.Visible = hints;
        _keys.Text = HudText.CombatHint + (client.LockOnAllowed ? HudText.LockOnHint : string.Empty);
        _hint!.Visible = hints;
    }

    private static Label Add(VBoxContainer column, Label label)
    {
        column.AddChild(label);
        return label;
    }

    private static void Fill(ColorRect bar, byte share) => bar.Size = new Vector2(BarWidth * share / 255f, BarHeight);

    private static Label Text(int size)
    {
        var label = new Label();
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        label.AddThemeConstantOverride("outline_size", 6);
        return label;
    }

    // A bar with its name on it: a dark back, the fill and the name over both.
    private static ColorRect Bar(VBoxContainer column, string name, Color colour)
    {
        var back = new ColorRect { Color = new Color(0, 0, 0, 0.6f), CustomMinimumSize = new Vector2(BarWidth, BarHeight), SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        var fill = new ColorRect { Color = colour, Size = new Vector2(BarWidth, BarHeight) };
        back.AddChild(fill);
        Label label = Text(14);
        label.Text = name;
        label.Position = new Vector2(6, -1);
        back.AddChild(label);
        column.AddChild(back);
        return fill;
    }
}
