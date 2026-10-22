namespace SumonerWorld.Core.Progression;

/// <summary>Each class's magic school (GDD §4, §11, E3a): a summoner casts only their school's spells.</summary>
public static class Schools
{
    /// <summary>The school of <paramref name="summonerClass"/>: Elves Primal, Humans Storm, Necromancers Umbral, Dwarves Ember.</summary>
    public static string Of(SummonerClass summonerClass) => summonerClass switch
    {
        SummonerClass.Elf => "primal",
        SummonerClass.Human => "storm",
        SummonerClass.Necromancer => "umbral",
        _ => "ember",
    };
}
