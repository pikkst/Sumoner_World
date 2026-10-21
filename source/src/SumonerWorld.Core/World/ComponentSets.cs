using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Progression;

namespace SumonerWorld.Core.World;

/// <summary>The component sets beyond the creature basics (combat, bonding, progression): registration and the state hash.</summary>
public static class ComponentSets
{
    /// <summary>Registers every set with a world.</summary>
    public static void Register(EcsWorld world)
    {
        BondingComponents.Register(world);
        ProgressionComponents.Register(world);
        CombatState.Register(world);
    }

    /// <summary>Adds every set to a state hash.</summary>
    public static void AddTo(ref StateHash hash, EcsWorld world)
    {
        ProgressionComponents.AddTo(ref hash, world);
        CombatState.AddTo(ref hash, world);
        BondingComponents.AddTo(ref hash, world);
    }
}
