using System.Numerics;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Progression;
using SumonerWorld.Core.Quests;
using SumonerWorld.Core.World;

namespace SumonerWorld.Core.Academy;

/// <summary>
/// The guaranteed first creature (Academy spec §4, M2-22): when a ranked student talks to the Keeper, the Keeper brings a young
/// creature of the student's class whose tier matches their rank; its species is the one the student's answers voted for most
/// (a tie goes to the seeded roll). It stands beside the Keeper, kept for that student: their starter Pact always succeeds,
/// anyone else's is refused.
/// </summary>
public static class FirstCreatureRules
{
    /// <summary>How far from the Keeper the creature arrives, in metres.</summary>
    public const float BesideMetres = 2.5f;

    /// <summary>The species the answers vote for (the most votes; a tie goes to <paramref name="roll"/>), or null when none.</summary>
    public static string? Species(AcademyDefinition academy, AcademyStanding standing, int roll)
    {
        ArgumentNullException.ThrowIfNull(academy);
        ArgumentNullException.ThrowIfNull(standing);
        var votes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        for (int q = 0; q < academy.Questions.Count; q++)
        {
            string creature = standing.Answers[q] == 0 ? string.Empty : academy.Questions[q].Answers[standing.Answers[q] - 1].Creature;
            if (creature.Length > 0)
            {
                votes[creature] = votes.GetValueOrDefault(creature) + 1;
            }
        }

        if (votes.Count == 0)
        {
            return null;
        }

        int best = votes.Values.Max();
        string[] tied = [.. votes.Where(vote => vote.Value == best).Select(vote => vote.Key)];
        return tied[Math.Abs(roll) % tied.Length];
    }

    /// <summary>True while the student's first-friend quest waits for the Keeper and no creature of theirs stands ready.</summary>
    public static bool Waiting(CreatureWorld world, ulong account)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (world.Data.Academy is not AcademyDefinition academy)
        {
            return false;
        }

        int quest = QuestIndex(world, academy.FirstFriendQuest);
        AcademyStanding standing = world.Economy.Academy.Of(account);
        bool active = quest >= 0 && world.Economy.Quests.Of(account).StatusOf(quest) == QuestStatus.Active;
        return active && (standing.FirstCreature == 0 || !world.TryFindCreature(new StableId(standing.FirstCreature), out _));
    }

    /// <summary>The Keeper at <paramref name="keeper"/> brings the student's first creature; returns it.</summary>
    public static Entity Bring(CreatureWorld world, Entity avatar, NpcDefinition keeper)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(keeper);
        AcademyDefinition academy = world.Data.Academy!;
        ulong account = world.World.GetComponent<PlayerAvatar>(avatar).AccountId;
        AcademyStanding standing = world.Economy.Academy.Of(account);
        string id = Species(academy, standing, world.Random.NextInt(0, 1000)) ?? world.Data.Species[0].Id;
        ushort species = world.SpeciesIndex(id);
        Vector3 player = world.World.GetComponent<Position>(avatar).Value;
        Vector2 toward = Vector2.Normalize(new Vector2(player.X - keeper.X, player.Z - keeper.Z) + new Vector2(0.001f, 0));
        var at = new Vector3(keeper.X + (toward.X * BesideMetres), 0, keeper.Z + (toward.Y * BesideMetres));
        at.Y = world.Settings.Ground?.HeightAt(at.X, at.Z) ?? player.Y;

        // Young and friendly: bold enough to stay, loyal, curious, gentle (it is still its own creature).
        Entity creature = world.SpawnCreature(species, at, new Personality(20, 60, 50, -40), group: 0);
        ref CreatureGrowth growth = ref world.World.GetComponent<CreatureGrowth>(creature);
        growth = growth with { Tier = world.World.GetComponent<PlayerProgress>(avatar).Rank, Level = 1, Stage = (int)LifeStage.Young };
        ProgressionComponents.Restat(world.Growth, world.Data, world.World, creature, world.Data.Species[species].Combat, ref world.World.GetComponent<Combatant>(creature));
        ref Combatant combatant = ref world.World.GetComponent<Combatant>(creature);
        combatant.Health = combatant.MaxHealth;
        standing.FirstCreature = world.World.GetComponent<StableId>(creature).Value;
        return creature;
    }

    private static int QuestIndex(CreatureWorld world, string id)
    {
        for (int i = 0; i < world.Data.Quests.Count; i++)
        {
            if (world.Data.Quests[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }
}
