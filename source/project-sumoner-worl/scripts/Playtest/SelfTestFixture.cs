using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Progression;
using SumonerWorld.Core.World;
using SumonerWorld.Net.Server;

namespace SumonerWorld.Game.Playtest;

/// <summary>
/// Test-only setup for <c>--playtest --self-test</c> (M2-06): the grove's hollow owl starts pacted to the test account, so the
/// self-test can summon and dismiss a companion and teach it a skill (level 4 at least, bond level 2) before the Academy gives every player their first creature (M2-22). Never used
/// outside the self-test.
/// </summary>
public static class SelfTestFixture
{
    /// <summary>Pacts the first hollow owl to <paramref name="account"/>; false when the world has none.</summary>
    public static bool GiveCompanion(GameServer server, ulong account)
    {
        ArgumentNullException.ThrowIfNull(server);
        CreatureWorld world = server.World;
        ushort owl = world.SpeciesIndex("hollow_owl");
        ComponentStore<SpeciesRef> species = world.World.Store<SpeciesRef>();
        ReadOnlySpan<Entity> creatures = species.Entities;
        for (int i = 0; i < creatures.Length; i++)
        {
            if (species.Components[i].Index == owl)
            {
                world.Services.Bonding.Restore(
                    world.World.GetComponent<MindRef>(creatures[i]).Handle,
                    new BondState(OwnershipState.Pacted, account, PactTerms.Of(0), 0, 2, false, (uint)world.Clock.Current.Index, 0, false));
                ref CreatureGrowth growth = ref world.World.GetComponent<CreatureGrowth>(creatures[i]);
                growth.Level = Math.Max(growth.Level, 4); // two skill points for the panel step (M2-10)
                return true;
            }
        }

        return false;
    }
}
