using Godot;
using SumonerWorld.Core.Data;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Game.Playtest;
using SumonerWorld.Net.Client;
using IntentKind = SumonerWorld.Net.Messages.IntentKind;

namespace SumonerWorld.Game.Ui;

/// <summary>
/// Trading with one of the Vale's people (E5b): their stock on the left with prices (buy one or five), and on the right the
/// things in the player's bag they buy, at their value (sell one or all). The player's gold is at the top. The server checks
/// every trade; the line under the window says how it went. It closes on Esc, Close, or when the player walks away.
/// </summary>
public sealed partial class ShopWindow(GameClient client, IDataRegistry data, InteractionControls controls, Func<int> personNear) : CanvasLayer
{
    private PanelContainer? _window;
    private Label? _title;
    private Label? _gold;
    private VBoxContainer? _stock;
    private VBoxContainer? _sell;
    private int _npc = -1;
    private bool _preview;
    private SumonerWorld.Net.Messages.InventoryList _shown;

    /// <summary>True while the window is shown.</summary>
    public bool IsOpen => _window?.Visible ?? false;

    /// <summary>What the window shows (for the self-test log).</summary>
    public string Describe() => _npc < 0 ? "closed"
        : $"{_title!.Text}: {string.Join(", ", ShopList.Offers(data, data.People[_npc]).Select(offer => $"{offer.Name} {offer.Price}g"))}; " +
          $"sells: {string.Join(", ", ShopList.Sellable(data, data.People[_npc], client.Inventory).Select(offer => $"{offer.Name} x{offer.Count}"))}; {_gold!.Text}";

    /// <summary>Opens the shop of person <paramref name="npc"/>; <paramref name="preview"/> (the self-test) keeps it open from afar.</summary>
    public void Open(int npc, bool preview = false)
    {
        _npc = npc;
        _preview = preview;
        _window!.Visible = true;
        Rebuild();
    }

    /// <summary>Closes the window.</summary>
    public void Close()
    {
        _window!.Visible = false;
        _npc = -1;
    }

    /// <summary>Buys <paramref name="count"/> of stock entry <paramref name="entry"/> (a click; also used by the self-test).</summary>
    public void Buy(int entry, int count)
    {
        ShopOffer offer = ShopList.Offers(data, data.People[_npc])[entry];
        controls.Send(IntentKind.Buy, ShopList.Target(_npc, count), $"{count} {offer.Name}", (byte)entry);
    }

    /// <summary>Sells <paramref name="count"/> from bag slot <paramref name="slot"/>.</summary>
    public void Sell(int slot, int count, string name) => controls.Send(IntentKind.Sell, ShopList.Target(_npc, count), $"{count} {name}", (byte)slot);

    /// <inheritdoc />
    public override void _Ready()
    {
        Layer = 7;
        _window = new PanelContainer { Visible = false };
        _window.AddThemeStyleboxOverride("panel", ItemLook.Window());
        _window.SetAnchorsPreset(Control.LayoutPreset.Center);
        _window.GrowHorizontal = Control.GrowDirection.Both;
        _window.GrowVertical = Control.GrowDirection.Both;
        _window.CustomMinimumSize = new Vector2(640, 0);
        AddChild(_window);
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 10);
        _window.AddChild(rows);
        _title = ItemLook.Text(string.Empty, 22);
        rows.AddChild(_title);
        _gold = ItemLook.Text(string.Empty, 17);
        _gold.AddThemeColorOverride("font_color", new Color(1, 0.85f, 0.4f));
        rows.AddChild(_gold);
        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 24);
        rows.AddChild(columns);
        _stock = Column(columns, "For sale");
        _sell = Column(columns, "They buy from you");
        var close = new Button { Text = "Close (Esc)", FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd };
        close.Pressed += Close;
        rows.AddChild(close);
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        if (IsOpen && @event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (!IsOpen)
        {
            return;
        }

        if (personNear() != _npc && !_preview)
        {
            Close();
            return;
        }

        if (!ReferenceEquals(client.Inventory.Entries, _shown.Entries) || client.Inventory.Gold != _shown.Gold)
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        _shown = client.Inventory;
        NpcDefinition person = data.People[_npc];
        _title!.Text = person.Name;
        _gold!.Text = $"Your gold: {client.Inventory.Gold}";
        Clear(_stock!);
        foreach (ShopOffer offer in ShopList.Offers(data, person))
        {
            HBoxContainer row = Row(_stock!, $"{offer.Name}  {offer.Price} gold");
            row.AddChild(Small("Buy", () => Buy(offer.Entry, 1)));
            if (data.Items[offer.Item].Stack > 1)
            {
                row.AddChild(Small("Buy 5", () => Buy(offer.Entry, 5)));
            }
        }

        if (person.Shop.Count == 0)
        {
            _stock!.AddChild(ItemLook.Text("Nothing for sale.", 15));
        }

        Clear(_sell!);
        foreach (SellOffer offer in ShopList.Sellable(data, person, client.Inventory))
        {
            HBoxContainer row = Row(_sell!, $"{offer.Name} x{offer.Count}  {offer.Value} gold each");
            row.AddChild(Small("Sell", () => Sell(offer.Slot, 1, offer.Name)));
            row.AddChild(Small("Sell all", () => Sell(offer.Slot, Math.Min(offer.Count, Core.Items.TradeRules.MaxCount), offer.Name)));
        }

        if (_sell!.GetChildCount() <= 1)
        {
            _sell.AddChild(ItemLook.Text(person.Buys.Count == 0 ? "They buy nothing." : $"Nothing they want. They buy: {string.Join(", ", person.Buys)}.", 15));
        }
    }

    private static VBoxContainer Column(HBoxContainer parent, string title)
    {
        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 6);
        Label heading = ItemLook.Text(title, 17);
        heading.AddThemeColorOverride("font_color", new Color(1, 0.85f, 0.5f));
        column.AddChild(heading);
        parent.AddChild(column);
        return column;
    }

    // Removes every row under the column's heading.
    private static void Clear(VBoxContainer column)
    {
        foreach (Node child in column.GetChildren().Skip(1))
        {
            child.QueueFree();
            column.RemoveChild(child);
        }
    }

    private static HBoxContainer Row(VBoxContainer column, string text)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        Label label = ItemLook.Text(text, 15);
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(label);
        column.AddChild(row);
        return row;
    }

    private static Button Small(string text, Action pressed)
    {
        var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
        button.AddThemeFontSizeOverride("font_size", 14);
        button.Pressed += pressed;
        return button;
    }
}
