using System.Numerics;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Ecs;

namespace SumonerWorld.Core.World;

/// <summary>
/// The M1 USP playtest scenario "Memory Grove" (QA plan B2): about 15 creatures within 60 m of where the player starts, placed to
/// give memory chances early without scripting any of them:
/// <list type="bullet">
/// <item>a grazer herd (mossback, 9, one calf snared 12 m north of the start);</item>
/// <item>a curious small predator pack (bogfox, 4, one snared 25 m east) that roams near the herd;</item>
/// <item>one solitary creature (hollow_owl) to the south.</item>
/// </list>
/// Placement and personalities come from the world's seeded stream, so every session starts with the same grove.
/// </summary>
public static class MemoryGrove
{
    /// <summary>Herd size (including the snared calf).</summary>
    public const int HerdSize = 9;

    /// <summary>Pack size (including the snared fox).</summary>
    public const int PackSize = 4;

    /// <summary>Creatures in the grove.</summary>
    public const int CreatureCount = HerdSize + PackSize + 1;

    /// <summary>Group id of the herd (packs and solitary creatures follow).</summary>
    public const int HerdGroup = 100;

    private const float Jitter = 6f;

    /// <summary>Spawns the grove around the player's start <paramref name="start"/>; returns the creatures, snared ones included.</summary>
    public static IReadOnlyList<Entity> Populate(CreatureWorld world, Vector3 start)
    {
        ArgumentNullException.ThrowIfNull(world);
        var creatures = new List<Entity>(CreatureCount);
        ushort mossback = world.SpeciesIndex("mossback");
        ushort bogfox = world.SpeciesIndex("bogfox");
        ushort owl = world.SpeciesIndex("hollow_owl");

        Entity calf = Spawn(world, mossback, start + new Vector3(0, 0, 12), HerdGroup, jitter: false);
        creatures.Add(calf);
        for (int i = 1; i < HerdSize; i++)
        {
            creatures.Add(Spawn(world, mossback, start + new Vector3(22, 0, 22), HerdGroup, jitter: true));
        }

        Entity fox = Spawn(world, bogfox, start + new Vector3(25, 0, 0), HerdGroup + 1, jitter: false);
        creatures.Add(fox);
        for (int i = 1; i < PackSize; i++)
        {
            creatures.Add(Spawn(world, bogfox, start + new Vector3(-30, 0, 15), HerdGroup + 1, jitter: true));
        }

        creatures.Add(Spawn(world, owl, start + new Vector3(5, 0, -40), HerdGroup + 2, jitter: false));
        world.Snare(calf, world.Clock.Current.Index);
        world.Snare(fox, world.Clock.Current.Index);
        return creatures;
    }

    private static Entity Spawn(CreatureWorld world, ushort species, Vector3 at, int group, bool jitter)
    {
        Vector3 position = jitter
            ? at + new Vector3((float)((world.Random.NextDouble() * 2) - 1) * Jitter, 0, (float)((world.Random.NextDouble() * 2) - 1) * Jitter)
            : at;
        return world.SpawnCreature(species, position, Personality.Roll(world.Data.Species[species], world.Random), group);
    }
}
