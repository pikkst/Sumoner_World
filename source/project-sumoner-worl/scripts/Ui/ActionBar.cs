using Godot;
using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Data;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Game.Combat;
using SumonerWorld.Game.Playtest;
using SumonerWorld.Net.Client;
using IntentKind = SumonerWorld.Net.Messages.IntentKind;

namespace SumonerWorld.Game.Ui;

/// <summary>
/// The action bar (M2-14b): ten buttons at the bottom of the screen on the keys 1–9 and 0, like in other MMOs. A button holds a
/// spell, a companion command, an item from the bag (food is eaten, gear put on) or, later, a companion's skill. Drag actions
/// onto it from the ability book (L), items from the bag (I); drag a button onto another to swap them, off the bar (or
/// right-click it) to clear it. The layout is saved per character on this computer (not in the self-test: empty file name).
/// </summary>
public sealed partial class ActionBar(
    GameClient client, IDataRegistry data, InteractionControls controls, CombatControls combat, CompanionBar companions, string layoutFile)
    : CanvasLayer
{
    private readonly UiSlot[] _buttons = new UiSlot[ActionLayout.Size];
    private ActionLayout _layout = ActionLayout.Default;

    /// <summary>The layout now.</summary>
    public ActionLayout Layout => _layout;

    /// <summary>The buttons as text, for the self-test log.</summary>
    public string Describe() => string.Join("  ", Enumerable.Range(0, ActionLayout.Size)
        .Select(i => $"{ActionLayout.KeyOf(i)}:{(_layout[i].IsEmpty ? "-" : InteractionText.ActionName(_layout[i]))}"));

    /// <summary>Runs button <paramref name="index"/> (0 = key 1, 9 = key 0), as its key does.</summary>
    public void Trigger(int index) => Run(_layout[index]);

    /// <summary>Runs an action (the bar's buttons and clicks in the ability book).</summary>
    public void Run(ActionRef action)
    {
        switch (action.Kind)
        {
            case ActionKind.Spell:
                combat.Cast(action.Id);
                break;
            case ActionKind.Command when Enum.TryParse(action.Id, out CompanionCommand command):
                companions.Command(command);
                break;
            case ActionKind.Item:
                UseItem(action.Id);
                break;
            default:
                break;
        }
    }

    /// <summary>A drop onto button <paramref name="index"/> (also used by the self-test).</summary>
    public void Drop(DragSpot from, int index)
    {
        switch (from.Place)
        {
            case DragPlace.Bar:
                _layout.Swap(from.Index, index);
                break;
            case DragPlace.Action:
                _layout.Set(index, ActionRef.Parse(from.Id));
                break;
            case DragPlace.Bag when DragRules.ItemIn(client.Inventory, from.Index) is int item:
                _layout.Set(index, new ActionRef(ActionKind.Item, data.Items[item].Id));
                break;
            default:
                return;
        }

        Save();
    }

    /// <summary>Clears button <paramref name="index"/>.</summary>
    public void Clear(int index)
    {
        _layout.Clear(index);
        Save();
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        Layer = 5;
        _layout = Load();
        var strip = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        strip.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        strip.OffsetTop = -UiSlot.SlotSize - 10;
        strip.OffsetBottom = -6;
        AddChild(strip);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        strip.AddChild(row);
        for (int i = 0; i < ActionLayout.Size; i++)
        {
            var button = new UiSlot(new DragSpot(DragPlace.Bar, 0, i), ActionLayout.KeyOf(i))
            {
                Accepts = (from, _) => from.Place is DragPlace.Bar or DragPlace.Action || (from.Place == DragPlace.Bag && from.Owner == 0),
                Dropped = (from, to) => Drop(from, to.Index),
                DraggedOut = spot => Clear(spot.Index),
                RightClicked = spot => Clear(spot.Index),
                Clicked = spot => Trigger(spot.Index),
            };
            _buttons[i] = button;
            row.AddChild(button);
        }
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false, ShiftPressed: false } key || controls.Menu.IsOpen
            || key.Keycode is < Key.Key0 or > Key.Key9)
        {
            return;
        }

        Trigger(ActionLayout.IndexOfDigit((int)(key.Keycode - Key.Key0)));
        GetViewport().SetInputAsHandled();
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        for (int i = 0; i < _buttons.Length; i++)
        {
            _buttons[i].Show(ContentOf(_layout[i]));
        }
    }

    private SlotContent? ContentOf(ActionRef action)
    {
        if (action.IsEmpty)
        {
            return null;
        }

        if (action.Kind == ActionKind.Item)
        {
            int item = data.ItemIndex(action.Id);
            int count = client.Inventory.Entries.Where(entry => entry.Item == item).Sum(entry => entry.Count);
            return ItemLook.Of(data, item, count) is SlotContent look ? look with { Count = count, Colour = count > 0 ? look.Colour : look.Colour.Darkened(0.6f) } : null;
        }

        string tooltip = action.Kind == ActionKind.Spell && data.Spells.FirstOrDefault(spell => spell.Id == action.Id) is SpellDefinition spell
            ? $"{InteractionText.ActionName(action)}\n{spell.Mana} mana"
            : InteractionText.ActionName(action);
        return ItemLook.Action(action, tooltip);
    }

    // Food is eaten, gear put on (oneself).
    private void UseItem(string id)
    {
        if (!data.Items.Any(item => item.Id == id))
        {
            return;
        }

        int item = data.ItemIndex(id);
        int slot = DragRules.SlotOf(client.Inventory, item);
        string name = InteractionText.SkillName(id);
        if (slot < 0)
        {
            controls.Say($"No {name} left in your bag.");
        }
        else if (data.Items[item].Restore.IsUsable)
        {
            controls.Send(IntentKind.UseItem, 0, name, (byte)slot);
        }
        else if (data.Items[item].Slot != EquipSlot.None)
        {
            controls.Send(IntentKind.Equip, 0, name, (byte)item);
        }
    }

    private ActionLayout Load()
    {
        if (layoutFile.Length == 0)
        {
            return ActionLayout.Default;
        }

        using Godot.FileAccess? file = Godot.FileAccess.Open(layoutFile, Godot.FileAccess.ModeFlags.Read);
        return file is null ? ActionLayout.Default : ActionLayout.Load(file.GetAsText());
    }

    private void Save()
    {
        if (layoutFile.Length == 0)
        {
            return;
        }

        using Godot.FileAccess? file = Godot.FileAccess.Open(layoutFile, Godot.FileAccess.ModeFlags.Write);
        file?.StoreString(_layout.Save());
    }
}
