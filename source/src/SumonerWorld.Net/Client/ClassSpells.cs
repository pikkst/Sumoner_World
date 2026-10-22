using SumonerWorld.Core.Data;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Progression;

namespace SumonerWorld.Net.Client;

/// <summary>
/// The spells a class casts (E3a): its school's, in data order. A layout made for another school (every character starts as a
/// student with the Elves' defaults, and the ranking may make them a Human, a Necromancer or a Dwarf) is moved to the class's
/// school: each foreign spell becomes the class's spell of the same shape (bolt, burst or ward).
/// </summary>
public static class ClassSpells
{
    /// <summary>The spell ids of <paramref name="summonerClass"/>'s school, in data order.</summary>
    public static IReadOnlyList<string> Of(IDataRegistry data, SummonerClass summonerClass)
    {
        ArgumentNullException.ThrowIfNull(data);
        string school = Schools.Of(summonerClass);
        var spells = new List<string>();
        foreach (SpellDefinition spell in data.Spells)
        {
            if (MoveOf(data, spell.Id)?.School == school)
            {
                spells.Add(spell.Id);
            }
        }

        return spells;
    }

    /// <summary>
    /// <paramref name="action"/> for <paramref name="summonerClass"/>: a spell of another school becomes the class's spell of the
    /// same shape (or empty if it has none); anything else stays.
    /// </summary>
    public static ActionRef Adapt(IDataRegistry data, ActionRef action, SummonerClass summonerClass)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (action.Kind != ActionKind.Spell || MoveOf(data, action.Id) is not MoveDefinition spell || spell.School == Schools.Of(summonerClass))
        {
            return action;
        }

        foreach (string own in Of(data, summonerClass))
        {
            if (MoveOf(data, own)?.Shape.Kind == spell.Shape.Kind)
            {
                return new ActionRef(ActionKind.Spell, own);
            }
        }

        return ActionRef.Empty;
    }

    /// <summary>Moves every button of <paramref name="layout"/> to <paramref name="summonerClass"/>'s spells; true if any changed.</summary>
    public static bool Adapt(IDataRegistry data, ActionLayout layout, SummonerClass summonerClass)
    {
        ArgumentNullException.ThrowIfNull(layout);
        bool changed = false;
        for (int i = 0; i < ActionLayout.Size; i++)
        {
            ActionRef adapted = Adapt(data, layout[i], summonerClass);
            if (adapted != layout[i])
            {
                layout.Set(i, adapted);
                changed = true;
            }
        }

        return changed;
    }

    private static MoveDefinition? MoveOf(IDataRegistry data, string id)
    {
        foreach (MoveDefinition move in data.Moves)
        {
            if (move.Id == id)
            {
                return move;
            }
        }

        return null;
    }
}
