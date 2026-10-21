using Godot;
using SumonerWorld.Core.Data;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Net.Client;

namespace SumonerWorld.Game.Ui;

/// <summary>How items and actions look in slots until there are icons (M2-14b): a name on a colour by kind.</summary>
public static class ItemLook
{
    /// <summary>An item in a slot, or null for an unknown index.</summary>
    public static SlotContent? Of(IDataRegistry data, int item, int count)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (item < 0 || item >= data.Items.Count)
        {
            return null;
        }

        ItemDefinition definition = data.Items[item];
        return new SlotContent(InteractionText.SkillName(definition.Id), count, ColourOf(definition.Category), InteractionText.ItemTooltip(definition));
    }

    /// <summary>The colour of an item kind.</summary>
    public static Color ColourOf(ItemCategory category) => category switch
    {
        ItemCategory.Food => new Color(0.45f, 0.3f, 0.15f, 0.95f),
        ItemCategory.Offering => new Color(0.45f, 0.2f, 0.45f, 0.95f),
        ItemCategory.Weapon => new Color(0.5f, 0.25f, 0.2f, 0.95f),
        ItemCategory.Armour => new Color(0.25f, 0.35f, 0.5f, 0.95f),
        ItemCategory.Charm => new Color(0.2f, 0.45f, 0.4f, 0.95f),
        _ => new Color(0.3f, 0.3f, 0.28f, 0.95f),
    };

    /// <summary>The colour of an action kind on the bar.</summary>
    public static Color ColourOf(ActionKind kind) => kind switch
    {
        ActionKind.Spell => new Color(0.2f, 0.4f, 0.25f, 0.95f),
        ActionKind.Command => new Color(0.45f, 0.35f, 0.15f, 0.95f),
        ActionKind.CompanionSkill => new Color(0.4f, 0.25f, 0.45f, 0.95f),
        _ => new Color(0.3f, 0.3f, 0.28f, 0.95f),
    };

    /// <summary>A spell or command in a slot.</summary>
    public static SlotContent Action(ActionRef action, string tooltip) =>
        new(InteractionText.ActionName(action), 0, ColourOf(action.Kind), tooltip);

    /// <summary>A dark panel style for windows.</summary>
    public static StyleBoxFlat Window()
    {
        var style = new StyleBoxFlat { BgColor = new Color(0.08f, 0.08f, 0.1f, 0.92f), BorderColor = new Color(0.55f, 0.5f, 0.4f) };
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(8);
        style.SetContentMarginAll(14);
        return style;
    }

    /// <summary>A label in the window style.</summary>
    public static Label Text(string text, int size = 15)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        return label;
    }
}
