using SumonerWorld.Core.Bonding;
using SumonerWorld.Net.Messages;

namespace SumonerWorld.Net.Client;

/// <summary>What an action bar button does (M2-14b).</summary>
public enum ActionKind
{
    /// <summary>Empty.</summary>
    None,

    /// <summary>Cast a spell (id = spell id).</summary>
    Spell,

    /// <summary>A command to the companions (id = <see cref="CompanionCommand"/> name).</summary>
    Command,

    /// <summary>Use or put on an item from the bag (id = item id).</summary>
    Item,

    /// <summary>Ask a companion for one of its learned moves (id = skill id; M2-14c).</summary>
    CompanionSkill,
}

/// <summary>One action: its kind and id, written <c>kind:id</c> (e.g. <c>spell:thornbolt</c>).</summary>
/// <param name="Kind">Kind.</param>
/// <param name="Id">Spell, command, item or skill id.</param>
public readonly record struct ActionRef(ActionKind Kind, string Id)
{
    /// <summary>The empty action.</summary>
    public static ActionRef Empty => new(ActionKind.None, string.Empty);

    /// <summary>True for an empty button.</summary>
    public bool IsEmpty => Kind == ActionKind.None;

    /// <summary>Reads <c>kind:id</c>; anything else is empty.</summary>
    public static ActionRef Parse(string? text)
    {
        int colon = text?.IndexOf(':', StringComparison.Ordinal) ?? -1;
        if (colon <= 0 || colon == text!.Length - 1)
        {
            return Empty;
        }

        string kind = text[..colon];
        ActionKind parsed = kind switch
        {
            "spell" => ActionKind.Spell,
            "cmd" => ActionKind.Command,
            "item" => ActionKind.Item,
            "skill" => ActionKind.CompanionSkill,
            _ => ActionKind.None,
        };
        return parsed == ActionKind.None ? Empty : new ActionRef(parsed, text[(colon + 1)..]);
    }

    /// <inheritdoc />
    public override string ToString() => Kind switch
    {
        ActionKind.Spell => $"spell:{Id}",
        ActionKind.Command => $"cmd:{Id}",
        ActionKind.Item => $"item:{Id}",
        ActionKind.CompanionSkill => $"skill:{Id}",
        _ => string.Empty,
    };
}

/// <summary>
/// The action bar's ten buttons, on the keys 1–9 and 0 (M2-14b): spells, companion commands, items and companion skills. The
/// player fills, moves and clears them by dragging; the layout is kept per character on the player's computer.
/// </summary>
public sealed class ActionLayout
{
    /// <summary>Buttons.</summary>
    public const int Size = 10;

    private readonly ActionRef[] _buttons = new ActionRef[Size];

    /// <summary>A new layout, empty.</summary>
    public ActionLayout()
    {
        Array.Fill(_buttons, ActionRef.Empty);
    }

    /// <summary>The first layout: the three spells on 1–3, food on 5, the commands on 7–0.</summary>
    public static ActionLayout Default
    {
        get
        {
            var layout = new ActionLayout();
            layout.Set(0, new ActionRef(ActionKind.Spell, "thornbolt"));
            layout.Set(1, new ActionRef(ActionKind.Spell, "entangle"));
            layout.Set(2, new ActionRef(ActionKind.Spell, "mend"));
            layout.Set(4, new ActionRef(ActionKind.Item, "berry_meal"));
            layout.Set(6, new ActionRef(ActionKind.Command, nameof(CompanionCommand.Attack)));
            layout.Set(7, new ActionRef(ActionKind.Command, nameof(CompanionCommand.GuardMe)));
            layout.Set(8, new ActionRef(ActionKind.Command, nameof(CompanionCommand.Hold)));
            layout.Set(9, new ActionRef(ActionKind.Command, nameof(CompanionCommand.Retreat)));
            return layout;
        }
    }

    /// <summary>The button at <paramref name="index"/> (0 = key 1, 9 = key 0).</summary>
    public ActionRef this[int index] => (uint)index < Size ? _buttons[index] : ActionRef.Empty;

    /// <summary>The key a button sits on: <c>1</c>…<c>9</c>, <c>0</c>.</summary>
    public static string KeyOf(int index) => index == Size - 1 ? "0" : (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The button for a number key's digit (1–9, 0), or −1.</summary>
    public static int IndexOfDigit(int digit) => digit switch
    {
        0 => Size - 1,
        >= 1 and <= 9 => digit - 1,
        _ => -1,
    };

    /// <summary>Puts an action on a button (an action already elsewhere on the bar stays there too).</summary>
    public void Set(int index, ActionRef action)
    {
        if ((uint)index < Size)
        {
            _buttons[index] = action;
        }
    }

    /// <summary>Empties a button.</summary>
    public void Clear(int index) => Set(index, ActionRef.Empty);

    /// <summary>Moves one button onto another; what was there goes to the first one's place.</summary>
    public void Swap(int from, int to)
    {
        if ((uint)from < Size && (uint)to < Size)
        {
            (_buttons[from], _buttons[to]) = (_buttons[to], _buttons[from]);
        }
    }

    /// <summary>One line per button, for saving.</summary>
    public string Save() => string.Join('\n', _buttons.Select(button => button.ToString()));

    /// <summary>Reads what <see cref="Save"/> wrote; unreadable lines are empty buttons.</summary>
    public static ActionLayout Load(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var layout = new ActionLayout();
        string[] lines = text.Split('\n');
        for (int i = 0; i < Math.Min(Size, lines.Length); i++)
        {
            layout.Set(i, ActionRef.Parse(lines[i].Trim()));
        }

        return layout;
    }
}

/// <summary>Where a dragged thing comes from or is dropped (M2-14b).</summary>
public enum DragPlace
{
    /// <summary>Nothing.</summary>
    None,

    /// <summary>A slot of the player's bag (index = slot).</summary>
    Bag,

    /// <summary>A slot of a companion's bag (owner = its stable id, index = slot).</summary>
    CreatureBag,

    /// <summary>An equipment position (owner = 0 for the player or a companion's stable id, index = position).</summary>
    Worn,

    /// <summary>An action bar button (index).</summary>
    Bar,

    /// <summary>An action from the ability book or the companion panel (id = <see cref="ActionRef"/> text).</summary>
    Action,
}

/// <summary>A drag's source or target, written as text for Godot's drag and drop.</summary>
/// <param name="Place">Where.</param>
/// <param name="Owner">Whose (a stable id; 0 = the player).</param>
/// <param name="Index">Slot, position or button.</param>
/// <param name="Id">For <see cref="DragPlace.Action"/>: the action.</param>
public readonly record struct DragSpot(DragPlace Place, ulong Owner, int Index, string Id = "")
{
    /// <summary>Writes <c>place|owner|index|id</c>.</summary>
    public override string ToString() => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Place}|{Owner}|{Index}|{Id}");

    /// <summary>Reads what <see cref="ToString"/> wrote; anything else is <see cref="DragPlace.None"/>.</summary>
    public static DragSpot Parse(string? text)
    {
        string[] parts = (text ?? string.Empty).Split('|', 4);
        return parts.Length == 4
            && Enum.TryParse(parts[0], out DragPlace place)
            && ulong.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out ulong owner)
            && int.TryParse(parts[2], System.Globalization.CultureInfo.InvariantCulture, out int index)
            ? new DragSpot(place, owner, index, parts[3])
            : default;
    }
}

/// <summary>An intent a drop asks for.</summary>
/// <param name="Kind">Intent.</param>
/// <param name="Target">Its target.</param>
/// <param name="Argument">Its argument.</param>
public readonly record struct DropIntent(IntentKind Kind, ulong Target, byte Argument);

/// <summary>What dropping one thing on another asks the server for (M2-14b); the server checks it all again.</summary>
public static class DragRules
{
    /// <summary>The intent for a drop between bags and equipment, or null when that drop does nothing on the server.</summary>
    public static DropIntent? IntentFor(DragSpot from, DragSpot to, InventoryList bag)
    {
        return (from.Place, to.Place) switch
        {
            (DragPlace.Bag, DragPlace.Bag) when from.Index != to.Index => new DropIntent(IntentKind.MoveItem, (ulong)from.Index, (byte)to.Index),
            (DragPlace.Bag, DragPlace.CreatureBag) => new DropIntent(IntentKind.TransferItem, to.Owner, (byte)from.Index),
            (DragPlace.CreatureBag, DragPlace.Bag) => new DropIntent(IntentKind.TransferItem, from.Owner, (byte)(from.Index | IntentArguments.FromCreature)),
            (DragPlace.Bag, DragPlace.Worn) when ItemIn(bag, from.Index) is int item => new DropIntent(IntentKind.Equip, to.Owner, (byte)item),
            (DragPlace.Worn, DragPlace.Bag) => new DropIntent(IntentKind.Unequip, from.Owner, (byte)from.Index),
            _ => null,
        };
    }

    /// <summary>The item in a slot of the bag, or null when it is empty.</summary>
    public static int? ItemIn(InventoryList bag, int slot)
    {
        ArgumentNullException.ThrowIfNull(bag.Entries);
        foreach (InventoryEntry entry in bag.Entries)
        {
            if (entry.Slot == slot)
            {
                return entry.Item;
            }
        }

        return null;
    }

    /// <summary>The first slot of the bag holding an item, or −1.</summary>
    public static int SlotOf(InventoryList bag, int item)
    {
        ArgumentNullException.ThrowIfNull(bag.Entries);
        foreach (InventoryEntry entry in bag.Entries)
        {
            if (entry.Item == item)
            {
                return entry.Slot;
            }
        }

        return -1;
    }
}
