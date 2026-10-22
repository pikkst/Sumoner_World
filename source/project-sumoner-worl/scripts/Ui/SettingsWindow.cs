using Godot;
using SumonerWorld.Game.Hosting;
using SumonerWorld.Net.Client;

namespace SumonerWorld.Game.Ui;

/// <summary>
/// The settings (M2-17): sound volume, mouse speed, text size and the key hints, in big controls. Esc opens it when no other window
/// is open (each window closes on Esc first) and closes it again. A change applies at once: the volume to the master sound bus,
/// the mouse speed to the <see cref="OrbitCamera"/>, the text size to every window. They are kept per account in
/// <paramref name="file"/> (empty: not kept, the self-test); the first time, the <see cref="KeyList"/> opens by itself.
/// </summary>
public sealed partial class SettingsWindow(OrbitCamera camera, string file) : CanvasLayer
{
    private readonly KeyList _keys = new();

    private PanelContainer? _window;
    private Label? _volume;
    private Label? _mouse;
    private Button? _text;
    private CheckBox? _hints;

    /// <summary>The key list (F1), a child, so it gets Esc before this window.</summary>
    public KeyList Keys => _keys;

    /// <summary>The settings now.</summary>
    public PlayerSettings Settings { get; private set; } = new();

    /// <summary>True while the window is shown.</summary>
    public bool IsOpen => _window?.Visible ?? false;

    /// <summary>The window's values as text (for the self-test log).</summary>
    public string Describe() => $"volume {Settings.Volume}%, mouse {Settings.MouseSpeed}%, text {Settings.TextSize}%, hints {(Settings.ShowHints ? "on" : "off")}";

    /// <summary>Opens or closes the window.</summary>
    public void Toggle() => _window!.Visible = !_window.Visible;

    /// <summary>Changes the settings (also used by the self-test): applies, shows and keeps them.</summary>
    public void Change(PlayerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Settings = settings.Clamped();
        Apply();
        Save();
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        Layer = 8;
        AddChild(_keys);
        if (file.Length > 0 && Godot.FileAccess.FileExists(file))
        {
            Settings = PlayerSettings.Parse(Godot.FileAccess.GetFileAsString(file));
        }

        Build();
        Apply();
        if (file.Length > 0 && !Settings.SeenKeys)
        {
            _keys.CallDeferred(KeyList.MethodName.Toggle);
            Change(Settings with { SeenKeys = true });
        }
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            Toggle();
            GetViewport().SetInputAsHandled();
        }
    }

    private void Build()
    {
        _window = new PanelContainer { Visible = false };
        _window.AddThemeStyleboxOverride("panel", ItemLook.Window());
        _window.SetAnchorsPreset(Control.LayoutPreset.Center);
        _window.GrowHorizontal = Control.GrowDirection.Both;
        _window.GrowVertical = Control.GrowDirection.Both;
        _window.CustomMinimumSize = new Vector2(460, 0);
        AddChild(_window);
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 14);
        _window.AddChild(rows);
        rows.AddChild(ItemLook.Text("Settings", 26));
        _volume = ItemLook.Text(string.Empty, 18);
        rows.AddChild(_volume);
        rows.AddChild(Slider(0, 100, 5, Settings.Volume, value => Change(Settings with { Volume = value })));
        _mouse = ItemLook.Text(string.Empty, 18);
        rows.AddChild(_mouse);
        rows.AddChild(Slider(PlayerSettings.MinMouseSpeed, PlayerSettings.MaxMouseSpeed, 5, Settings.MouseSpeed, value => Change(Settings with { MouseSpeed = value })));
        _text = Big(string.Empty, () => Change(Settings.NextTextSize()));
        rows.AddChild(_text);
        _hints = new CheckBox { Text = "Show key hints on screen", ButtonPressed = Settings.ShowHints, FocusMode = Control.FocusModeEnum.None };
        _hints.AddThemeFontSizeOverride("font_size", 18);
        _hints.Toggled += on => Change(Settings with { ShowHints = on });
        rows.AddChild(_hints);
        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 12);
        buttons.AddChild(Big("All keys (F1)", _keys.Toggle));
        buttons.AddChild(Big("Close (Esc)", Toggle));
        rows.AddChild(buttons);
    }

    private void Apply()
    {
        int master = AudioServer.GetBusIndex("Master");
        AudioServer.SetBusVolumeDb(master, Settings.VolumeDb);
        AudioServer.SetBusMute(master, Settings.Volume == 0);
        camera.MouseFactor = Settings.MouseFactor;
        GetTree().Root.ContentScaleFactor = Settings.TextScale;
        _volume!.Text = Settings.Volume == 0 ? "Sound: off" : $"Sound: {Settings.Volume}%";
        _mouse!.Text = $"Mouse speed: {Settings.MouseSpeed}%";
        _text!.Text = $"Text size: {Settings.TextSize}% (click to change)";
        _hints!.SetPressedNoSignal(Settings.ShowHints);
    }

    private void Save()
    {
        if (file.Length == 0)
        {
            return;
        }

        using Godot.FileAccess? stream = Godot.FileAccess.Open(file, Godot.FileAccess.ModeFlags.Write);
        stream?.StoreString(Settings.ToString());
    }

    private static HSlider Slider(int min, int max, int step, int value, Action<int> changed)
    {
        var slider = new HSlider { MinValue = min, MaxValue = max, Step = step, Value = value, CustomMinimumSize = new Vector2(400, 28), FocusMode = Control.FocusModeEnum.None };
        slider.ValueChanged += moved => changed((int)moved);
        return slider;
    }

    private static Button Big(string text, Action pressed)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 40), FocusMode = Control.FocusModeEnum.None };
        button.AddThemeFontSizeOverride("font_size", 18);
        foreach ((string state, float light) in new[] { ("normal", 0.22f), ("hover", 0.3f), ("pressed", 0.16f) })
        {
            var style = new StyleBoxFlat { BgColor = new Color(light, light, light * 0.9f), BorderColor = new Color(0.55f, 0.5f, 0.4f) };
            style.SetBorderWidthAll(1);
            style.SetCornerRadiusAll(6);
            style.SetContentMarginAll(8);
            button.AddThemeStyleboxOverride(state, style);
        }

        button.Pressed += pressed;
        return button;
    }
}
