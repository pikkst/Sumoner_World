using Godot;
using SumonerWorld.Core.Data;
using SumonerWorld.Core.World;
using SumonerWorld.Game.Creatures;
using SumonerWorld.Net.Client;

namespace SumonerWorld.Game.DevTools;

/// <summary>
/// The client's dev tools (M1-22, dev builds and <c>--dev-tools</c>): the <b>dev overlay</b> (F3) with frame stats and the server's
/// tick, stage and AI timings, and the <b>AI Inspector</b> (F4): click a creature (or Tab for the next one near the player) to see
/// its traits, needs, emotions, strongest memories, opinions, goal scores and running behaviour-tree node, from the server twice a
/// second (<see cref="GameClient.RequestDevTools"/>). A ring marks the inspected creature. Escape closes the inspector.
/// </summary>
public sealed partial class DevToolsLayer : CanvasLayer
{
    private const double WindowSeconds = 0.5;
    private const float PickRadiusPixels = 48;
    private const int FontSize = 13;

    private readonly GameClient _client;
    private readonly EntityViewSync _views;
    private readonly IDataRegistry _data;
    private readonly Func<Vector3> _player;
    private Label? _overlay;
    private Label? _inspector;
    private PanelContainer? _inspectorPanel;
    private MeshInstance3D? _ring;
    private double _window;
    private double _frameSum;
    private double _frameMax;
    private int _frames;
    private ClientFrameStats _stats;

    /// <summary>Creates the dev tools; <paramref name="player"/> gives the own character's position (for Tab).</summary>
    public DevToolsLayer(GameClient client, EntityViewSync views, IDataRegistry data, Func<Vector3> player)
    {
        _client = client;
        _views = views;
        _data = data;
        _player = player;
        Name = "DevTools";
        Layer = 10;
    }

    /// <summary>Whether the overlay is shown.</summary>
    public bool OverlayVisible => _overlay?.Visible == true;

    /// <summary>The creature being inspected (0 = none).</summary>
    public ulong Inspected { get; private set; }

    /// <summary>Shows or hides the overlay.</summary>
    public void ShowOverlay(bool visible)
    {
        _overlay!.Visible = visible;
        Request();
    }

    /// <summary>Inspects a creature (0 closes the inspector).</summary>
    public void Inspect(ulong creature)
    {
        Inspected = creature;
        _inspectorPanel!.Visible = creature != 0;
        Request();
    }

    /// <summary>Inspects the creature nearest to the player; false when none is shown.</summary>
    public bool InspectNearest()
    {
        ulong best = 0;
        float bestDistance = float.MaxValue;
        Vector3 player = _player();
        foreach ((ulong id, Node3D view) in _views.Creatures)
        {
            float distance = view.GlobalPosition.DistanceTo(player);
            if (distance < bestDistance)
            {
                (best, bestDistance) = (id, distance);
            }
        }

        Inspect(best);
        return best != 0;
    }

    /// <summary>
    /// Self-test of the controls (<c>--inspect=click|tab</c>): queues the same input events a player makes, so they go through
    /// <see cref="_UnhandledInput"/>. <c>click</c>: F3, then a left click on the nearest creature; returns its id.
    /// <c>tab</c>: Tab twice (the second-nearest creature ends up inspected).
    /// </summary>
    public ulong PressForTest(string how)
    {
        if (how == "tab")
        {
            Press(Key.Tab);
            Press(Key.Tab);
            return 0;
        }

        Press(Key.F3);
        Camera3D? camera = GetViewport().GetCamera3D();
        Vector3 player = _player();
        (ulong id, Node3D view) = _views.Creatures
            .Where(entry => camera is not null && !camera.IsPositionBehind(entry.View.GlobalPosition))
            .OrderBy(entry => entry.View.GlobalPosition.DistanceTo(player))
            .FirstOrDefault();
        if (camera is null || view is null)
        {
            return 0;
        }

        Vector2 screen = camera.UnprojectPosition(view.GlobalPosition + new Vector3(0, 0.8f, 0));
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = screen, GlobalPosition = screen });
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = screen, GlobalPosition = screen });
        return id;
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        var font = new SystemFont { FontNames = ["Consolas", "DejaVu Sans Mono", "Liberation Mono", "monospace"] };
        _overlay = new Label { Name = "Overlay", Position = new Vector2(12, 10), Visible = false };
        Style(_overlay, font);
        AddChild(_overlay);

        _inspectorPanel = new PanelContainer { Name = "Inspector", Visible = false };
        _inspectorPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.06f, 0.08f, 0.78f),
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 8,
            ContentMarginBottom = 8,
        });
        _inspectorPanel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _inspectorPanel.GrowHorizontal = Control.GrowDirection.Begin;
        _inspectorPanel.Position = new Vector2(-12, 10);
        _inspector = new Label();
        Style(_inspector, font);
        _inspectorPanel.AddChild(_inspector);
        AddChild(_inspectorPanel);

        _ring = new MeshInstance3D
        {
            Name = "InspectedRing",
            Mesh = new TorusMesh { InnerRadius = 1.1f, OuterRadius = 1.35f, Rings = 24, RingSegments = 6 },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(1, 0.85f, 0.2f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
            Visible = false,
        };
        _views.AddChild(_ring);
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.F3 }:
                ShowOverlay(!OverlayVisible);
                break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.F4 }:
                if (Inspected == 0)
                {
                    InspectNearest();
                }
                else
                {
                    Inspect(0);
                }

                break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Tab }:
                InspectNext();
                break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape } when Inspected != 0:
                Inspect(0);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click:
                Pick(click.Position);
                break;
            default:
                return;
        }

        GetViewport().SetInputAsHandled();
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        _frames++;
        _frameSum += delta;
        _frameMax = Math.Max(_frameMax, delta);
        _window += delta;
        if (_window >= WindowSeconds)
        {
            _stats = new ClientFrameStats(
                _frames / _window,
                _frameSum / _frames * 1000,
                _frameMax * 1000,
                _views.Shown,
                _client.LastSnapshotTick,
                _client.LastSnapshotTick - _client.Remote.RenderTick);
            (_window, _frameSum, _frameMax, _frames) = (0, 0, 0, 0);
        }

        if (OverlayVisible)
        {
            _overlay!.Text = DevReadout.Overlay(_stats, _client.DevMetrics);
        }

        UpdateInspector();
    }

    private static void Style(Label label, Font font)
    {
        label.AddThemeFontOverride("font", font);
        label.AddThemeFontSizeOverride("font_size", FontSize);
        label.AddThemeColorOverride("font_color", new Color(0.92f, 0.95f, 0.9f));
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        label.AddThemeConstantOverride("outline_size", 4);
    }

    private void UpdateInspector()
    {
        bool shown = Inspected != 0 && _views.TryGetCreature(Inspected, out Node3D? view);
        _ring!.Visible = shown;
        if (shown && _views.TryGetCreature(Inspected, out view))
        {
            _ring.GlobalPosition = view!.GlobalPosition + new Vector3(0, 0.08f, 0);
        }

        if (Inspected == 0)
        {
            return;
        }

        _inspector!.Text = _client.Inspection is CreatureInspection inspection
            ? DevReadout.Inspector(inspection, _data)
            : _client.InspectionGone ? $"creature #{Inspected} is gone" : $"creature #{Inspected}: waiting for the server…";
    }

    private void InspectNext()
    {
        Vector3 player = _player();
        List<ulong> nearest = [.. _views.Creatures.OrderBy(entry => entry.View.GlobalPosition.DistanceTo(player)).Select(entry => entry.Id).Take(16)];
        if (nearest.Count == 0)
        {
            return;
        }

        int index = nearest.IndexOf(Inspected);
        Inspect(nearest[(index + 1) % nearest.Count]);
    }

    private void Pick(Vector2 screen)
    {
        Camera3D? camera = GetViewport().GetCamera3D();
        if (camera is null)
        {
            return;
        }

        ulong best = 0;
        float bestPixels = PickRadiusPixels;
        foreach ((ulong id, Node3D view) in _views.Creatures)
        {
            Vector3 centre = view.GlobalPosition + new Vector3(0, 0.8f, 0);
            if (camera.IsPositionBehind(centre))
            {
                continue;
            }

            float pixels = camera.UnprojectPosition(centre).DistanceTo(screen);
            if (pixels < bestPixels)
            {
                (best, bestPixels) = (id, pixels);
            }
        }

        if (best != 0)
        {
            Inspect(best);
        }
    }

    private static void Press(Key key)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = key, Pressed = false });
    }

    private void Request() => _client.RequestDevTools(OverlayVisible, Inspected);
}
