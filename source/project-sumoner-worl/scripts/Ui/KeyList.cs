using Godot;
using SumonerWorld.Net.Client;

namespace SumonerWorld.Game.Ui;

/// <summary>
/// Every key in big letters, in groups side by side (M2-17, <see cref="HudText.KeyList"/>). F1 opens and closes it, and so do
/// Esc and the Close button; it also opens by itself the first time a player starts the game. The groups take as many columns as
/// fit the screen at the chosen text size, and the list scrolls when it is taller than the screen.
/// </summary>
public sealed partial class KeyList : CanvasLayer
{
    private const float Margin = 60;

    private PanelContainer? _window;
    private GridContainer? _groups;
    private ScrollContainer? _scroll;

    /// <summary>True while the list is shown.</summary>
    public bool IsOpen => _window?.Visible ?? false;

    /// <summary>Opens or closes the list.</summary>
    public void Toggle()
    {
        _window!.Visible = !_window.Visible;
        if (_window.Visible)
        {
            Fit();
        }
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        Layer = 8;
        _window = new PanelContainer { Visible = false };
        _window.AddThemeStyleboxOverride("panel", ItemLook.Window());
        _window.SetAnchorsPreset(Control.LayoutPreset.Center);
        _window.GrowHorizontal = Control.GrowDirection.Both;
        _window.GrowVertical = Control.GrowDirection.Both;
        AddChild(_window);
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 10);
        _window.AddChild(rows);
        rows.AddChild(ItemLook.Text("Keys", 26));
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        rows.AddChild(_scroll);
        _groups = new GridContainer();
        _groups.AddThemeConstantOverride("h_separation", 32);
        _groups.AddThemeConstantOverride("v_separation", 14);
        _scroll.AddChild(_groups);
        foreach ((string title, IReadOnlyList<(string Keys, string Does)> keys) in HudText.KeyList)
        {
            var group = new VBoxContainer();
            Label heading = ItemLook.Text(title, 20);
            heading.AddThemeColorOverride("font_color", new Color(1, 0.85f, 0.5f));
            group.AddChild(heading);
            var lines = new GridContainer { Columns = 2 };
            lines.AddThemeConstantOverride("h_separation", 14);
            foreach ((string key, string does) in keys)
            {
                Label name = ItemLook.Text(key, 17);
                name.AddThemeColorOverride("font_color", new Color(0.75f, 0.9f, 1));
                lines.AddChild(name);
                lines.AddChild(ItemLook.Text(does, 17));
            }

            group.AddChild(lines);
            _groups.AddChild(group);
        }

        var close = new Button { Text = "Close (F1)", SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd, FocusMode = Control.FocusModeEnum.None };
        close.AddThemeFontSizeOverride("font_size", 18);
        close.Pressed += Toggle;
        rows.AddChild(close);
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false } key && (key.Keycode == Key.F1 || (IsOpen && key.Keycode == Key.Escape)))
        {
            Toggle();
            GetViewport().SetInputAsHandled();
        }
    }

    // As many columns as fit the visible width (it shrinks as the text grows), and no taller than the screen.
    private void Fit()
    {
        Vector2 screen = GetViewport().GetVisibleRect().Size;
        float widest = 0;
        foreach (Node group in _groups!.GetChildren())
        {
            widest = Mathf.Max(widest, ((Control)group).GetCombinedMinimumSize().X);
        }

        float separation = _groups.GetThemeConstant("h_separation");
        _groups.Columns = Math.Clamp((int)((screen.X - Margin + separation) / (widest + separation)), 1, 3);
        Vector2 size = _groups.GetCombinedMinimumSize();
        _scroll!.CustomMinimumSize = new Vector2(size.X, Mathf.Min(size.Y, screen.Y - 160));
    }
}
