using SumonerWorld.Net.Client;
using SumonerWorld.Net.Messages;

namespace SumonerWorld.Tests.Net;

public class ActionLayoutTests
{
    [Fact]
    public void The_default_bar_has_spells_on_1_to_3_food_on_5_and_commands_on_7_to_0()
    {
        ActionLayout layout = ActionLayout.Default;

        Assert.Equal(new ActionRef(ActionKind.Spell, "thornbolt"), layout[ActionLayout.IndexOfDigit(1)]);
        Assert.Equal(new ActionRef(ActionKind.Item, "berry_meal"), layout[ActionLayout.IndexOfDigit(5)]);
        Assert.Equal(new ActionRef(ActionKind.Command, "Retreat"), layout[ActionLayout.IndexOfDigit(0)]);
        Assert.True(layout[ActionLayout.IndexOfDigit(4)].IsEmpty);
        Assert.Equal(("1", "9", "0"), (ActionLayout.KeyOf(0), ActionLayout.KeyOf(8), ActionLayout.KeyOf(9)));
    }

    [Fact]
    public void Buttons_can_be_swapped_cleared_set_and_saved()
    {
        ActionLayout layout = ActionLayout.Default;
        layout.Swap(0, 3);
        layout.Clear(1);
        layout.Set(5, new ActionRef(ActionKind.Item, "sweet_berries"));

        ActionLayout loaded = ActionLayout.Load(layout.Save());

        Assert.Equal(new ActionRef(ActionKind.Spell, "thornbolt"), loaded[3]);
        Assert.True(loaded[0].IsEmpty);
        Assert.True(loaded[1].IsEmpty);
        Assert.Equal(new ActionRef(ActionKind.Item, "sweet_berries"), loaded[5]);
        Assert.True(ActionLayout.Load("rubbish\nspell:\n:x")[0].IsEmpty);
    }

    [Fact]
    public void Drops_between_bags_and_equipment_ask_for_the_matching_intent()
    {
        var bag = new InventoryList(0, [new InventoryEntry(7, 1, 2), new InventoryEntry(3, 5, 4)]);
        var slot2 = new DragSpot(DragPlace.Bag, 0, 2);

        Assert.Equal(new DropIntent(IntentKind.MoveItem, 2, 9), DragRules.IntentFor(slot2, new DragSpot(DragPlace.Bag, 0, 9), bag));
        Assert.Equal(new DropIntent(IntentKind.TransferItem, 55, 2), DragRules.IntentFor(slot2, new DragSpot(DragPlace.CreatureBag, 55, 0), bag));
        Assert.Equal(
            new DropIntent(IntentKind.TransferItem, 55, 1 | IntentArguments.FromCreature),
            DragRules.IntentFor(new DragSpot(DragPlace.CreatureBag, 55, 1), slot2, bag));
        Assert.Equal(new DropIntent(IntentKind.Equip, 55, 7), DragRules.IntentFor(slot2, new DragSpot(DragPlace.Worn, 55, 4), bag));
        Assert.Equal(new DropIntent(IntentKind.Unequip, 0, 3), DragRules.IntentFor(new DragSpot(DragPlace.Worn, 0, 3), slot2, bag));
        Assert.Null(DragRules.IntentFor(new DragSpot(DragPlace.Bag, 0, 1), new DragSpot(DragPlace.Worn, 0, 4), bag));
        Assert.Null(DragRules.IntentFor(slot2, slot2, bag));
        Assert.Equal(4, DragRules.SlotOf(bag, 3));
    }

    [Fact]
    public void Drag_spots_round_trip_as_text()
    {
        var spot = new DragSpot(DragPlace.Action, 12, 3, "spell:mend");

        Assert.Equal(spot, DragSpot.Parse(spot.ToString()));
        Assert.Equal(DragPlace.None, DragSpot.Parse("nonsense").Place);
    }
}
