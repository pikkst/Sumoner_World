using SumonerWorld.Core.Data;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Progression;
using SumonerWorld.Scenarios;

namespace SumonerWorld.Tests.Core.Data;

/// <summary>The class creatures (E3b): every class has its own, each complete in data.</summary>
public class ClassCreaturesTests
{
    private static readonly SummonerClass[] Classes = [SummonerClass.Elf, SummonerClass.Human, SummonerClass.Necromancer, SummonerClass.Dwarf];

    [Fact]
    public void Every_class_has_at_least_two_creatures_it_can_bond()
    {
        foreach (SummonerClass summonerClass in Classes)
        {
            Assert.True(
                Bondable().Count(species => species.Class == summonerClass) >= 2,
                $"{summonerClass} needs at least two creatures");
        }

        Assert.Equal(["storm_hawk", "plains_hound"], Bondable().Where(species => species.Class == SummonerClass.Human).Select(species => species.Id).Order().Reverse());
    }

    [Fact]
    public void Every_bondable_creature_has_a_look_a_skill_tree_loot_and_moves_of_its_kind()
    {
        DataRegistry data = ShippedData.Current;
        for (int i = 0; i < data.Species.Count; i++)
        {
            SpeciesDefinition species = data.Species[i];
            if (species.Id == "training_construct")
            {
                continue;
            }

            Assert.NotNull(species.Look);
            Assert.NotNull(data.SkillTreeOf(i));
            Assert.NotNull(data.LootTableOf(i));
            Assert.Equal(2, data.SkillTreeOf(i)!.Nodes.Count(node => node.Move is not null));
            Assert.All(species.Combat.Moves, move => Assert.True(data.MoveIndex(move) >= 0));
        }
    }

    [Fact]
    public void Class_creatures_fight_with_their_school()
    {
        DataRegistry data = ShippedData.Current;
        Assert.Equal(DamageType.Storm, data.Moves[data.MoveIndex("storm_dive")].DamageType);
        Assert.Equal(DamageType.Umbral, data.Moves[data.MoveIndex("spirit_bite")].DamageType);
        Assert.Equal(30, data.Moves[data.MoveIndex("spirit_bite")].DrainPercent);
        Assert.Equal(("burning", DamageType.Ember), (data.Moves[data.MoveIndex("flame_lick")].Effect, data.Moves[data.MoveIndex("flame_lick")].DamageType));
    }

    [Fact]
    public void A_look_with_a_bad_colour_or_feature_is_refused()
    {
        List<DataFile> files = [.. ShippedData.Files];
        int index = files.FindIndex(file => file.Path.EndsWith("storm_hawk.json", StringComparison.Ordinal));
        files[index] = files[index] with
        {
            Content = files[index].Content
                .Replace("\"colour\": \"#6E7F96\"", "\"colour\": \"blue\"", StringComparison.Ordinal)
                .Replace("\"feature\": \"wings\"", "\"feature\": \"tentacles\"", StringComparison.Ordinal),
        };

        var error = Assert.Throws<DataValidationException>(() => DataRegistryLoader.Load(files));

        Assert.Contains(error.Errors, message => message.Contains("look.colour 'blue'", StringComparison.Ordinal));
        Assert.Contains(error.Errors, message => message.Contains("look needs a feature", StringComparison.Ordinal));
    }

    private static IEnumerable<SpeciesDefinition> Bondable() => ShippedData.Current.Species.Where(species => species.Id != "training_construct");
}
