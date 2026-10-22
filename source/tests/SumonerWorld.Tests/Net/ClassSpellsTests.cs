using SumonerWorld.Core.Progression;
using SumonerWorld.Net.Client;
using SumonerWorld.Scenarios;

namespace SumonerWorld.Tests.Net;

public class ClassSpellsTests
{
    [Fact]
    public void Each_class_has_its_schools_three_spells()
    {
        Assert.Equal(["entangle", "mend", "thornbolt"], ClassSpells.Of(ShippedData.Current, SummonerClass.Elf));
        Assert.Equal(["spark", "tailwind", "thunderclap"], ClassSpells.Of(ShippedData.Current, SummonerClass.Human));
        Assert.Equal(["shade_grip", "soulbolt", "veil"], ClassSpells.Of(ShippedData.Current, SummonerClass.Necromancer));
        Assert.Equal(["cinder_burst", "ember_shield", "firebolt"], ClassSpells.Of(ShippedData.Current, SummonerClass.Dwarf));
    }

    [Fact]
    public void A_students_bar_moves_to_the_class_school_shape_for_shape_and_keeps_the_rest()
    {
        ActionLayout layout = ActionLayout.Default;

        Assert.True(ClassSpells.Adapt(ShippedData.Current, layout, SummonerClass.Dwarf));

        Assert.Equal("spell:firebolt spell:cinder_burst spell:ember_shield", $"{layout[0]} {layout[1]} {layout[2]}");
        Assert.Equal(ActionLayout.Default[4], layout[4]);
        Assert.Equal(ActionLayout.Default[6], layout[6]);
        Assert.False(ClassSpells.Adapt(ShippedData.Current, layout, SummonerClass.Dwarf), "a second time changes nothing");
        Assert.False(ClassSpells.Adapt(ShippedData.Current, ActionLayout.Default, SummonerClass.Elf));
    }
}
