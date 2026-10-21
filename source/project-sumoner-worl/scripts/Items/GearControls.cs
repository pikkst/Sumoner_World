using System.Text;
using Godot;
using SumonerWorld.Core.Data;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Items;
using SumonerWorld.Game.Playtest;
using SumonerWorld.Net.Client;
using SumonerWorld.Net.Messages;
using IntentKind = SumonerWorld.Net.Messages.IntentKind;

namespace SumonerWorld.Game.Items;

/// <summary>
/// Crafting and gear on the keyboard (crafting spec §6 and §8, M2-14). B opens the crafting panel: every recipe with what it needs,
/// a number key starts making it, and the panel shows what is being made. While the inventory (I) is open, the gear part below it
/// lists what the chosen wearer wears and the gear in the bag it can wear: a number key puts that on, Shift + a number takes off
/// what is worn at that position, O switches the wearer between the player and the companions at their side. The server checks
/// everything; the result lines come through the <see cref="InteractionControls"/>.
/// </summary>
public sealed partial class GearControls(GameClient client, IDataRegistry data, InteractionControls controls, Func<bool> inventoryOpen) : CanvasLayer
{
    private Label? _crafting;
    private Label? _gear;
    private int _wearer;
    private int _wasCrafting;
    private int _outputBefore;

    /// <summary>True while the crafting panel is shown.</summary>
    public bool CraftingOpen => _crafting?.Visible ?? false;

    /// <summary>The crafting panel's text as shown (for the self-test log).</summary>
    public string CraftingText => _crafting?.Text ?? string.Empty;

    /// <summary>The gear panel's text as shown (for the self-test log).</summary>
    public string GearText => _gear?.Text ?? string.Empty;

    /// <summary>Opens or closes the crafting panel.</summary>
    public void ToggleCrafting() => _crafting!.Visible = !_crafting.Visible;

    /// <summary>Starts making recipe <paramref name="recipe"/> (also used by the self-test).</summary>
    public void Craft(int recipe)
    {
        if (recipe >= 0 && recipe < data.Recipes.Count)
        {
            controls.Send(IntentKind.Craft, 0, InteractionText.SkillName(data.Recipes[recipe].Output.Item), (byte)recipe);
        }
    }

    /// <summary>Puts on the <paramref name="number"/>th gear item listed for the chosen wearer (also used by the self-test).</summary>
    public void Equip(int number)
    {
        List<int> wearable = Wearable(out ulong wearer);
        if (number >= 0 && number < wearable.Count)
        {
            controls.Send(IntentKind.Equip, wearer, InteractionText.SkillName(data.Items[wearable[number]].Id), (byte)wearable[number]);
        }
    }

    /// <summary>Takes off what the chosen wearer wears at a position.</summary>
    public void Unequip(EquipPosition position)
    {
        int item = Worn(out ulong wearer)[(int)position];
        if (item >= 0 && item < data.Items.Count)
        {
            controls.Send(IntentKind.Unequip, wearer, InteractionText.SkillName(data.Items[item].Id), (byte)position);
        }
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        Layer = 5;
        _crafting = new Label { Visible = false };
        _crafting.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        _crafting.Position += new Vector2(470, 20);
        Style(_crafting);
        AddChild(_crafting);
        _gear = new Label { Visible = false };
        _gear.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _gear.Position += new Vector2(-300, 330);
        Style(_gear);
        AddChild(_gear);
    }

    /// <inheritdoc />
    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key || controls.Menu.IsOpen)
        {
            return;
        }

        int number = key.Keycode is >= Key.Key1 and <= Key.Key9 ? (int)(key.Keycode - Key.Key1) : -1;
        if (key.Keycode == Key.B)
        {
            ToggleCrafting();
        }
        else if (CraftingOpen && key.Keycode == Key.Escape)
        {
            ToggleCrafting();
        }
        else if (CraftingOpen && number >= 0)
        {
            Craft(number);
        }
        else if (inventoryOpen() && key.Keycode == Key.O)
        {
            _wearer++;
        }
        else if (inventoryOpen() && number >= 0 && key.ShiftPressed)
        {
            Unequip((EquipPosition)Math.Min(number, Equipment.Positions - 1));
        }
        else if (inventoryOpen() && number >= 0)
        {
            Equip(number);
        }
        else
        {
            return;
        }

        GetViewport().SetInputAsHandled();
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        int crafting = client.Inventory.Crafting;
        if (crafting != _wasCrafting && crafting > 0 && crafting - 1 < data.Recipes.Count)
        {
            _outputBefore = CountOf(data.Recipes[crafting - 1].Output.Item);
        }
        else if (_wasCrafting > 0 && crafting == 0 && _wasCrafting - 1 < data.Recipes.Count)
        {
            string output = data.Recipes[_wasCrafting - 1].Output.Item;
            controls.Say(InteractionText.CraftEnded(InteractionText.SkillName(output), CountOf(output) > _outputBefore));
        }

        _wasCrafting = crafting;
        if (CraftingOpen)
        {
            _crafting!.Text = DescribeRecipes(crafting);
        }

        _gear!.Visible = inventoryOpen();
        if (_gear.Visible)
        {
            _gear.Text = DescribeGear();
        }
    }

    private int CountOf(string item)
    {
        int index = data.ItemIndex(item);
        return client.Inventory.Entries.Where(entry => entry.Item == index).Sum(entry => entry.Count);
    }

    private string DescribeRecipes(int crafting)
    {
        var text = new StringBuilder("Crafting (by hand)\n");
        for (int i = 0; i < data.Recipes.Count; i++)
        {
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"[{i + 1}] {InteractionText.RecipeLine(data.Recipes[i])}\n");
        }

        if (crafting > 0 && crafting - 1 < data.Recipes.Count)
        {
            text.Append("Making: ").Append(InteractionText.SkillName(data.Recipes[crafting - 1].Output.Item)).Append("...\n");
        }

        return text.Append("[1-9] make   [B/Esc] close").ToString();
    }

    private string DescribeGear()
    {
        WornItems worn = Worn(out _);
        var text = new StringBuilder($"Gear of {WearerName()}   [O] other\n");
        for (int i = 0; i < Equipment.Positions; i++)
        {
            if (worn[i] >= 0 && worn[i] < data.Items.Count)
            {
                text.Append(System.Globalization.CultureInfo.InvariantCulture, $"[Shift+{i + 1}] {InteractionText.PositionName((EquipPosition)i)}: {InteractionText.SkillName(data.Items[worn[i]].Id)}\n");
            }
        }

        List<int> wearable = Wearable(out _);
        for (int i = 0; i < wearable.Count; i++)
        {
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"[{i + 1}] put on {InteractionText.SkillName(data.Items[wearable[i]].Id)}\n");
        }

        if (wearable.Count == 0)
        {
            text.Append("(no gear it can wear in the bag)\n");
        }

        return text.ToString();
    }

    // The player first, then the companions at their side; the choice wraps around.
    private CompanionEntry? ChosenCompanion()
    {
        List<CompanionEntry> summoned = [.. client.Companions.Where(companion => companion.Summoned)];
        int choice = _wearer % (summoned.Count + 1);
        return choice == 0 ? null : summoned[choice - 1];
    }

    private string WearerName() => ChosenCompanion() is CompanionEntry companion && companion.Species < data.Species.Count
        ? $"your {InteractionText.DisplayName(data.Species[companion.Species].Id)}"
        : "you";

    private WornItems Worn(out ulong wearer)
    {
        CompanionEntry? companion = ChosenCompanion();
        wearer = companion?.Id ?? 0;
        return companion?.Worn ?? client.Inventory.Worn;
    }

    // Gear items in the bag the chosen wearer can wear, in bag order.
    private List<int> Wearable(out ulong wearer)
    {
        CompanionEntry? companion = ChosenCompanion();
        wearer = companion?.Id ?? 0;
        string kind = companion is CompanionEntry entry && entry.Species < data.Species.Count
            ? data.Species[entry.Species].BodyType.ToString().ToLowerInvariant()
            : "player";
        var items = new List<int>();
        foreach (InventoryEntry stack in client.Inventory.Entries)
        {
            if (stack.Item < data.Items.Count && data.Items[stack.Item] is ItemDefinition item && item.Slot != EquipSlot.None
                && item.Wearers.Contains(kind) && !items.Contains(stack.Item))
            {
                items.Add(stack.Item);
            }
        }

        return items;
    }

    private static void Style(Label label)
    {
        label.AddThemeFontSizeOverride("font_size", 16);
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        label.AddThemeConstantOverride("outline_size", 6);
    }
}
