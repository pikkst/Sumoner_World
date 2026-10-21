using Godot;
using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Data;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Net.Client;

namespace SumonerWorld.Game.Playtest;

/// <summary>
/// The Pact menu (M2-05, AI Bible §10.2): the promises a player can make, one checkbox per Pact Term from the data, up to
/// <see cref="PactTermDefinition.MaxTermsPerOffer"/>. Number keys or clicks choose, Enter or the button offers, Esc cancels. It never
/// shows a chance or a score (M0-08): the creature answers with its body, then a line of text.
/// </summary>
public sealed partial class PactMenu : CanvasLayer
{
    private readonly IDataRegistry _data;
    private readonly Action<PactTerms> _offer;
    private readonly List<CheckBox> _boxes = [];
    private PanelContainer? _panel;
    private Label? _title;
    private Button? _send;

    /// <summary>Creates the menu; <paramref name="offer"/> sends the chosen terms.</summary>
    public PactMenu(IDataRegistry data, Action<PactTerms> offer)
    {
        _data = data;
        _offer = offer;
        Name = "PactMenu";
        Layer = 6;
    }

    /// <summary>True while the menu is shown.</summary>
    public bool IsOpen => _panel?.Visible ?? false;

    /// <summary>The terms chosen now.</summary>
    public PactTerms Chosen
    {
        get
        {
            byte mask = 0;
            for (int i = 0; i < _boxes.Count; i++)
            {
                mask |= _boxes[i].ButtonPressed ? (byte)(1 << i) : (byte)0;
            }

            return new PactTerms(mask);
        }
    }

    /// <summary>Shows the menu for a creature, with nothing chosen.</summary>
    public void Open(string creature)
    {
        _title!.Text = InteractionText.PactMenuTitle(creature);
        foreach (CheckBox box in _boxes)
        {
            box.ButtonPressed = false;
        }

        Refresh();
        _panel!.Visible = true;
    }

    /// <summary>Hides the menu without offering.</summary>
    public void Close() => _panel!.Visible = false;

    /// <inheritdoc />
    public override void _Ready()
    {
        _panel = new PanelContainer { Visible = false };
        _panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        _panel.GrowHorizontal = Control.GrowDirection.Both;
        _panel.GrowVertical = Control.GrowDirection.Both;
        AddChild(_panel);

        var margin = new MarginContainer();
        foreach (string side in new[] { "left", "right", "top", "bottom" })
        {
            margin.AddThemeConstantOverride($"margin_{side}", 18);
        }

        _panel.AddChild(margin);
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 8);
        margin.AddChild(rows);

        _title = new Label();
        _title.AddThemeFontSizeOverride("font_size", 20);
        rows.AddChild(_title);
        for (int i = 0; i < _data.PactTerms.Count; i++)
        {
            var box = new CheckBox { Text = $"[{i + 1}]  {InteractionText.PactTerm(_data.PactTerms[i].Key)}", FocusMode = Control.FocusModeEnum.None };
            box.AddThemeFontSizeOverride("font_size", 18);
            box.Toggled += _ => Refresh();
            rows.AddChild(box);
            _boxes.Add(box);
        }

        _send = new Button { Text = "Offer the Pact", FocusMode = Control.FocusModeEnum.None };
        _send.AddThemeFontSizeOverride("font_size", 18);
        _send.Pressed += Offer;
        rows.AddChild(_send);
        var hints = new Label { Text = InteractionText.PactMenuHints };
        hints.AddThemeFontSizeOverride("font_size", 14);
        rows.AddChild(hints);
    }

    /// <inheritdoc />
    public override void _Input(InputEvent @event)
    {
        if (!IsOpen || @event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }

        long number = key.Keycode - Key.Key1;
        if (number >= 0 && number < _boxes.Count)
        {
            CheckBox box = _boxes[(int)number];
            if (!box.Disabled)
            {
                box.ButtonPressed = !box.ButtonPressed;
            }
        }
        else if (key.Keycode is Key.Enter or Key.KpEnter)
        {
            Offer();
        }
        else if (key.Keycode == Key.Escape)
        {
            Close();
        }
        else
        {
            return;
        }

        GetViewport().SetInputAsHandled();
    }

    // At most three promises: the other boxes are greyed out once three are chosen; nothing to offer without one.
    private void Refresh()
    {
        int chosen = Chosen.Count;
        foreach (CheckBox box in _boxes)
        {
            box.Disabled = !box.ButtonPressed && chosen >= PactTermDefinition.MaxTermsPerOffer;
        }

        _send!.Disabled = chosen == 0;
    }

    private void Offer()
    {
        if (Chosen.Count == 0)
        {
            return;
        }

        _offer(Chosen);
        Close();
    }
}
