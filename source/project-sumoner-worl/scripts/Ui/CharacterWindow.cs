using System.Globalization;
using System.Text;
using Godot;
using SumonerWorld.Core.Data;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Items;
using SumonerWorld.Game.Playtest;
using SumonerWorld.Net.Client;
using SumonerWorld.Net.Messages;
using IntentKind = SumonerWorld.Net.Messages.IntentKind;

namespace SumonerWorld.Game.Ui;

/// <summary>
/// The character window (M2-14b, like the bag and character sheet of other MMOs): I opens it. On the left the chosen wearer, the
/// player or one of the companions at their side (tabs at the top), drawn with their equipment slots where they are worn: head
/// at the top, body in the middle, weapon and off hand at the sides, charms below; a creature shows only the slots its body has,
/// and its own bag. On the right the player's bag (24 slots), gold and weight. Drag items between the bag, the slots and the
/// companion's bag; right-click an item to use it or put it on the chosen wearer, a worn item to take it off. The server checks
/// every move; I or Esc closes it.
/// </summary>
public sealed partial class CharacterWindow(GameClient client, IDataRegistry data, InteractionControls controls) : CanvasLayer
{
    private const int KilogramsGrams = 1000;
    private const int BagColumns = 6;

    // Where each equipment position sits on the figure, as (x, y) in the figure's box.
    private static readonly Vector2[] SlotAt =
    [
        new(22, 130), new(222, 130), new(122, 8), new(122, 128), new(52, 252), new(192, 252),
    ];

    private readonly List<UiSlot> _bag = [];
    private readonly List<UiSlot> _worn = [];
    private readonly List<UiSlot> _creatureBag = [];
    private PanelContainer? _window;
    private HBoxContainer? _tabs;
    private Figure? _figure;
    private Label? _who;
    private Label? _gold;
    private Label? _weight;
    private VBoxContainer? _creatureBox;
    private GridContainer? _creatureGrid;
    private int _choice;
    private (ulong Id, string Kind, int Slots) _built = (ulong.MaxValue, string.Empty, -1);
    private string _tabsFor = string.Empty;
    private readonly Core.Progression.GrowthRules _growth = new(data.Combat.Growth);

    /// <summary>True while the window is shown.</summary>
    public bool IsOpen => _window?.Visible ?? false;

    /// <summary>The wearer chosen now: 0 for the player, else a companion's stable id.</summary>
    public ulong Wearer => Chosen()?.Id ?? 0;

    /// <summary>Opens or closes the window.</summary>
    public void Toggle() => _window!.Visible = !_window.Visible;

    /// <summary>Chooses the wearer tab <paramref name="index"/> (0 = the player, then the companions at their side).</summary>
    public void Choose(int index) => _choice = Math.Max(0, index);

    /// <summary>What the window shows, as text (for the self-test log).</summary>
    public string Describe()
    {
        var text = new StringBuilder().Append(_who?.Text).Append(": ");
        text.AppendJoin(", ", _worn.Where(slot => slot.Visible && slot.Content is not null).Select(slot => $"{slot.Hint} = {slot.Content!.Value.Name}"));
        text.Append("; bag: ").AppendJoin(", ", _bag.Where(slot => slot.Content is not null).Select(slot => $"{slot.Content!.Value.Name} x{Math.Max(1, slot.Content!.Value.Count)}"));
        if (_creatureBox?.Visible ?? false)
        {
            text.Append("; its bag: ").AppendJoin(", ", _creatureBag.Where(slot => slot.Content is not null).Select(slot => slot.Content!.Value.Name));
        }

        return text.Append(CultureInfo.InvariantCulture, $"; {_gold?.Text}").ToString();
    }

    /// <summary>A drop from one spot onto another, as a mouse drop does (also used by the self-test).</summary>
    public void Drop(DragSpot from, DragSpot to)
    {
        if (DragRules.IntentFor(from, to, client.Inventory) is DropIntent intent)
        {
            controls.Send(intent.Kind, intent.Target, NameAt(from), intent.Argument);
        }
    }

    /// <summary>The spot of the chosen wearer's equipment position.</summary>
    public DragSpot WornSpot(EquipPosition position) => new(DragPlace.Worn, Wearer, (int)position);

    /// <inheritdoc />
    public override void _Ready()
    {
        Layer = 6;
        _window = new PanelContainer { Visible = false };
        _window.AddThemeStyleboxOverride("panel", ItemLook.Window());
        _window.SetAnchorsPreset(Control.LayoutPreset.Center);
        _window.GrowHorizontal = Control.GrowDirection.Both;
        _window.GrowVertical = Control.GrowDirection.Both;
        AddChild(_window);
        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 24);
        _window.AddChild(columns);
        columns.AddChild(BuildLeft());
        columns.AddChild(BuildRight());
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key || controls.Menu.IsOpen)
        {
            return;
        }

        if (key.Keycode == Key.I || (IsOpen && key.Keycode == Key.Escape))
        {
            Toggle();
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

        RebuildTabs();
        CompanionEntry? companion = Chosen();
        string kind = companion is CompanionEntry entry && entry.Species < data.Species.Count
            ? data.Species[entry.Species].BodyType.ToString().ToLowerInvariant()
            : "player";
        int creatureSlots = companion is CompanionEntry pet && pet.Species < data.Species.Count
            ? pet.Bag?.Slots ?? CreatureBags.SizeOf(data.Species[pet.Species].BodyType).Slots
            : 0;
        if (_built != (Wearer, kind, creatureSlots))
        {
            Rebuild(kind, creatureSlots);
        }

        Refresh(companion);
    }

    private VBoxContainer BuildLeft()
    {
        var left = new VBoxContainer();
        left.AddThemeConstantOverride("separation", 8);
        _tabs = new HBoxContainer();
        _tabs.AddThemeConstantOverride("separation", 4);
        left.AddChild(_tabs);
        _who = ItemLook.Text(string.Empty, 15);
        left.AddChild(_who);
        _figure = new Figure { CustomMinimumSize = new Vector2(300, 320) };
        left.AddChild(_figure);
        for (int i = 0; i < Equipment.Positions; i++)
        {
            var position = (EquipPosition)i;
            var slot = new UiSlot(new DragSpot(DragPlace.Worn, 0, i), hint: InteractionText.SkillName(InteractionText.PositionName(position)))
            {
                Position = SlotAt[i],
            };
            Wire(slot);
            _worn.Add(slot);
            _figure.AddChild(slot);
        }

        _creatureBox = new VBoxContainer { Visible = false };
        _creatureBox.AddChild(ItemLook.Text("Its bag", 14));
        _creatureGrid = new GridContainer { Columns = 4 };
        _creatureBox.AddChild(_creatureGrid);
        left.AddChild(_creatureBox);
        return left;
    }

    private VBoxContainer BuildRight()
    {
        var right = new VBoxContainer();
        right.AddThemeConstantOverride("separation", 8);
        _gold = ItemLook.Text("Bag", 17);
        right.AddChild(_gold);
        var grid = new GridContainer { Columns = BagColumns };
        grid.AddThemeConstantOverride("h_separation", 4);
        grid.AddThemeConstantOverride("v_separation", 4);
        right.AddChild(grid);
        for (int i = 0; i < Inventory.Slots; i++)
        {
            var slot = new UiSlot(new DragSpot(DragPlace.Bag, 0, i));
            Wire(slot);
            _bag.Add(slot);
            grid.AddChild(slot);
        }

        _weight = ItemLook.Text(string.Empty, 13);
        right.AddChild(_weight);
        right.AddChild(ItemLook.Text("Drag to move or put on   right-click: use / put on / take off\nDrag items onto the bar to put them on a key   [I/Esc] close", 12));
        return right;
    }

    private void Wire(UiSlot slot)
    {
        slot.Accepts = (from, to) => DragRules.IntentFor(from, to, client.Inventory) is not null;
        slot.Dropped = Drop;
        slot.RightClicked = RightClick;
    }

    // Tabs: you, then each companion at your side.
    private void RebuildTabs()
    {
        List<CompanionEntry> summoned = Summoned();
        string key = string.Join(",", summoned.Select(companion => companion.Id));
        if (key == _tabsFor)
        {
            return;
        }

        _tabsFor = key;
        foreach (Node child in _tabs!.GetChildren())
        {
            child.QueueFree();
        }

        AddTab("You", 0);
        for (int i = 0; i < summoned.Count; i++)
        {
            AddTab(SpeciesName(summoned[i]), i + 1);
        }
    }

    private void AddTab(string name, int index)
    {
        var tab = new Button { Text = name, FocusMode = Control.FocusModeEnum.None };
        tab.Pressed += () => Choose(index);
        _tabs!.AddChild(tab);
    }

    private void Rebuild(string kind, int creatureSlots)
    {
        _built = (Wearer, kind, creatureSlots);
        _figure!.Kind = kind;
        _figure.QueueRedraw();
        for (int i = 0; i < _worn.Count; i++)
        {
            _worn[i].Visible = EquipRules.Has(kind, (EquipPosition)i);
        }

        foreach (UiSlot slot in _creatureBag)
        {
            slot.QueueFree();
        }

        _creatureBag.Clear();
        _creatureBox!.Visible = creatureSlots > 0;
        for (int i = 0; i < creatureSlots; i++)
        {
            var slot = new UiSlot(new DragSpot(DragPlace.CreatureBag, Wearer, i));
            Wire(slot);
            _creatureBag.Add(slot);
            _creatureGrid!.AddChild(slot);
        }

        // Worn slots carry the wearer in their spot: replace them in place.
        for (int i = 0; i < _worn.Count; i++)
        {
            UiSlot old = _worn[i];
            var slot = new UiSlot(new DragSpot(DragPlace.Worn, Wearer, i), hint: old.Hint) { Position = old.Position, Visible = old.Visible };
            Wire(slot);
            _figure.AddChild(slot);
            old.QueueFree();
            _worn[i] = slot;
        }
    }

    private void Refresh(CompanionEntry? companion)
    {
        InventoryList inventory = client.Inventory;
        int grams = 0;
        for (int i = 0; i < _bag.Count; i++)
        {
            InventoryEntry? entry = inventory.Entries.FirstOrDefault(e => e.Slot == i) is { Count: > 0 } found ? found : null;
            _bag[i].Show(entry is InventoryEntry shown ? ItemLook.Of(data, shown.Item, shown.Count) : null);
            grams += entry is InventoryEntry weighed && weighed.Item < data.Items.Count ? data.Items[weighed.Item].WeightGrams * weighed.Count : 0;
        }

        WornItems worn = companion?.Worn ?? inventory.Worn;
        for (int i = 0; i < _worn.Count; i++)
        {
            _worn[i].Show(worn[i] >= 0 ? ItemLook.Of(data, worn[i], 1) : null);
        }

        IReadOnlyList<InventoryEntry> pet = companion?.Bag?.Entries ?? [];
        for (int i = 0; i < _creatureBag.Count; i++)
        {
            InventoryEntry? entry = pet.FirstOrDefault(e => e.Slot == i) is { Count: > 0 } found ? found : null;
            _creatureBag[i].Show(entry is InventoryEntry shown ? ItemLook.Of(data, shown.Item, shown.Count) : null);
        }

        _who!.Text = companion is CompanionEntry chosen ? $"Your {SpeciesName(chosen)}, level {chosen.Level}"
            : client.OwnProfile is RosterEntry profile ? InteractionText.ProfileLines(profile, _growth, data.Combat)
            : "You";
        _gold!.Text = string.Create(CultureInfo.InvariantCulture, $"Bag   {inventory.Gold} gold");
        _weight!.Text = string.Create(CultureInfo.InvariantCulture, $"{grams / (float)KilogramsGrams:F1} / {Inventory.MaxWeightGrams / KilogramsGrams} kg");
    }

    // Right-click: use or put on from the bag, take off what is worn, take back from the companion's bag.
    private void RightClick(DragSpot spot)
    {
        switch (spot.Place)
        {
            case DragPlace.Bag when DragRules.ItemIn(client.Inventory, spot.Index) is int item:
                string name = InteractionText.SkillName(data.Items[item].Id);
                if (data.Items[item].Restore.IsUsable)
                {
                    controls.Send(IntentKind.UseItem, Wearer, name, (byte)spot.Index);
                }
                else if (data.Items[item].Slot != EquipSlot.None)
                {
                    controls.Send(IntentKind.Equip, Wearer, name, (byte)item);
                }

                break;
            case DragPlace.Worn:
                Drop(spot, new DragSpot(DragPlace.Bag, 0, 0));
                break;
            case DragPlace.CreatureBag:
                Drop(spot, new DragSpot(DragPlace.Bag, 0, 0));
                break;
            default:
                break;
        }
    }

    private string NameAt(DragSpot spot)
    {
        int item = spot.Place switch
        {
            DragPlace.Bag => DragRules.ItemIn(client.Inventory, spot.Index) ?? -1,
            DragPlace.Worn => (Chosen()?.Worn ?? client.Inventory.Worn)[spot.Index],
            DragPlace.CreatureBag => Chosen()?.Bag?.Entries.FirstOrDefault(entry => entry.Slot == spot.Index).Item ?? -1,
            _ => -1,
        };
        return item >= 0 && item < data.Items.Count ? InteractionText.SkillName(data.Items[item].Id) : "it";
    }

    private List<CompanionEntry> Summoned() => [.. client.Companions.Where(companion => companion.Summoned)];

    private CompanionEntry? Chosen()
    {
        List<CompanionEntry> summoned = Summoned();
        return _choice >= 1 && _choice <= summoned.Count ? summoned[_choice - 1] : null;
    }

    private string SpeciesName(CompanionEntry companion) =>
        companion.Species < data.Species.Count ? InteractionText.DisplayName(data.Species[companion.Species].Id) : "companion";

    /// <summary>The wearer's outline behind the slots: a person, or a creature by its body.</summary>
    private sealed partial class Figure : Control
    {
        private static readonly Color Shade = new(0.35f, 0.38f, 0.42f, 0.6f);

        public string Kind { get; set; } = "player";

        public override void _Draw()
        {
            switch (Kind)
            {
                case "player":
                    DrawCircle(new Vector2(151, 96), 24, Shade);
                    DrawRect(new Rect2(116, 124, 70, 120), Shade);
                    DrawRect(new Rect2(92, 128, 22, 96), Shade);
                    DrawRect(new Rect2(188, 128, 22, 96), Shade);
                    DrawRect(new Rect2(120, 246, 26, 66), Shade);
                    DrawRect(new Rect2(156, 246, 26, 66), Shade);
                    break;
                case "avian":
                    DrawCircle(new Vector2(151, 120), 26, Shade);
                    DrawCircle(new Vector2(151, 190), 56, Shade);
                    DrawRect(new Rect2(136, 244, 8, 40), Shade);
                    DrawRect(new Rect2(158, 244, 8, 40), Shade);
                    break;
                default:
                    DrawCircle(new Vector2(151, 96), 30, Shade);
                    DrawRect(new Rect2(70, 140, 162, 90), Shade);
                    DrawRect(new Rect2(80, 230, 20, 60), Shade);
                    DrawRect(new Rect2(202, 230, 20, 60), Shade);
                    break;
            }
        }
    }
}
