using Godot;
using SumonerWorld.Net.Client;

namespace SumonerWorld.Game.Ui;

/// <summary>What a slot shows: a short name, a count (0 = none shown), a colour and a tooltip.</summary>
/// <param name="Name">Name.</param>
/// <param name="Count">How many (shown when above 1).</param>
/// <param name="Colour">Background colour.</param>
/// <param name="Tooltip">Tooltip text.</param>
public readonly record struct SlotContent(string Name, int Count, Color Colour, string Tooltip);

/// <summary>
/// One square slot of a bag, an equipment position or the action bar (M2-14b). It can be dragged onto another slot and takes
/// drops; what a drop does is up to its owner. Dragging it out onto nothing calls <see cref="DraggedOut"/> (the action bar
/// clears the button), right-clicking calls <see cref="RightClicked"/>.
/// </summary>
public sealed partial class UiSlot : Panel
{
    /// <summary>Slot size in pixels.</summary>
    public const float SlotSize = 58;

    private static readonly Color Empty = new(0.12f, 0.12f, 0.14f, 0.85f);
    private static readonly Color Border = new(0.55f, 0.5f, 0.4f);

    private readonly Label _name = new();
    private readonly Label _count = new();
    private readonly Label _key = new();
    private readonly StyleBoxFlat _style = new();
    private SlotContent? _content;
    private bool _dragging;

    /// <summary>Creates a slot.</summary>
    public UiSlot(DragSpot spot, string key = "", string hint = "")
    {
        Spot = spot;
        CustomMinimumSize = new Vector2(SlotSize, SlotSize);
        _style.BgColor = Empty;
        _style.BorderColor = Border;
        _style.SetBorderWidthAll(2);
        _style.SetCornerRadiusAll(5);
        AddThemeStyleboxOverride("panel", _style);
        _name.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _name.HorizontalAlignment = HorizontalAlignment.Center;
        _name.VerticalAlignment = VerticalAlignment.Center;
        _name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _name.AddThemeFontSizeOverride("font_size", 11);
        _name.MouseFilter = MouseFilterEnum.Ignore;
        _count.Position = new Vector2(SlotSize - 16, SlotSize - 19);
        _count.AddThemeFontSizeOverride("font_size", 12);
        _count.MouseFilter = MouseFilterEnum.Ignore;
        _key.Position = new Vector2(4, 1);
        _key.Text = key;
        _key.AddThemeFontSizeOverride("font_size", 11);
        _key.AddThemeColorOverride("font_color", new Color(1, 0.9f, 0.6f));
        _key.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_name);
        AddChild(_count);
        AddChild(_key);
        Hint = hint;
        TooltipText = hint;
        _name.Text = hint;
        _name.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
    }

    /// <summary>Where this slot is.</summary>
    public DragSpot Spot { get; }

    /// <summary>Shown when empty (e.g. <c>Head</c>).</summary>
    public string Hint { get; }

    /// <summary>Asked whether a drag from a spot may be dropped here.</summary>
    public Func<DragSpot, DragSpot, bool>? Accepts { get; set; }

    /// <summary>Called with (from, to) when something is dropped here.</summary>
    public Action<DragSpot, DragSpot>? Dropped { get; set; }

    /// <summary>Called when this slot was dragged onto nothing.</summary>
    public Action<DragSpot>? DraggedOut { get; set; }

    /// <summary>Called on a right click.</summary>
    public Action<DragSpot>? RightClicked { get; set; }

    /// <summary>Called on a left click (without dragging).</summary>
    public Action<DragSpot>? Clicked { get; set; }

    /// <summary>What it shows now (null = empty).</summary>
    public SlotContent? Content => _content;

    /// <summary>Shows <paramref name="content"/> (null = empty).</summary>
    public void Show(SlotContent? content)
    {
        if (content == _content)
        {
            return;
        }

        _content = content;
        _style.BgColor = content?.Colour ?? Empty;
        _name.Text = content?.Name ?? Hint;
        _name.AddThemeColorOverride("font_color", content is null ? new Color(0.5f, 0.5f, 0.5f) : new Color(1, 1, 1));
        _count.Text = content is SlotContent shown && shown.Count > 1 ? shown.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;
        TooltipText = content?.Tooltip ?? Hint;
    }

    /// <inheritdoc />
    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (_content is not SlotContent content)
        {
            return default;
        }

        var preview = new Label { Text = content.Name };
        preview.AddThemeFontSizeOverride("font_size", 14);
        preview.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        preview.AddThemeConstantOverride("outline_size", 5);
        SetDragPreview(preview);
        _dragging = true;
        return Spot.ToString();
    }

    /// <inheritdoc />
    public override bool _CanDropData(Vector2 atPosition, Variant data) =>
        data.VariantType == Variant.Type.String && DragSpot.Parse(data.AsString()) is DragSpot from && from.Place != DragPlace.None
        && (Accepts?.Invoke(from, Spot) ?? false);

    /// <inheritdoc />
    public override void _DropData(Vector2 atPosition, Variant data) => Dropped?.Invoke(DragSpot.Parse(data.AsString()), Spot);

    /// <inheritdoc />
    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Right })
        {
            RightClicked?.Invoke(Spot);
            AcceptEvent();
        }
        else if (@event is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left } && !_dragging)
        {
            Clicked?.Invoke(Spot);
        }
    }

    /// <inheritdoc />
    public override void _Notification(int what)
    {
        if (what == NotificationDragEnd && _dragging)
        {
            _dragging = false;
            if (!GetViewport().GuiIsDragSuccessful())
            {
                DraggedOut?.Invoke(Spot);
            }
        }
    }
}
