using SumonerWorld.Core.Movement;
using SumonerWorld.Core.Navigation;
using SumonerWorld.Core.World;
using SumonerWorld.Net.Server;

namespace SumonerWorld.Game.World;

/// <summary>
/// The M1 dev world every host builds the same way (TDD §5.1): the <see cref="DevRegion"/> ground, created once per process and shared
/// (it is read-only), and the movement settings on it. The server, the client's prediction and the bots must stand on the same ground,
/// or prediction would be corrected every tick.
/// </summary>
public static class DevWorld
{
    private const int DevGrazers = 170;
    private const int DevGrazerHerds = 10;
    private const int DevPredators = 30;
    private const int DevPredatorPacks = 5;

    private static readonly Lazy<Heightfield> SharedGround = new(() => DevRegion.CreateGround());

    /// <summary>The 512 m dev region's ground.</summary>
    public static Heightfield Ground => SharedGround.Value;

    /// <summary>Player movement on the dev ground (server and clients).</summary>
    public static MovementSettings Movement() => new() { Ground = Ground };

    /// <summary>
    /// Fills a new world: the playtest gets the <see cref="MemoryGrove"/> around the player's start; the dev server 170 seeded
    /// mossbacks in 10 herds and 30 bogfoxes in 5 packs across the region. Returns a description for the log.
    /// </summary>
    public static string Populate(GameServer server, bool playtest)
    {
        ArgumentNullException.ThrowIfNull(server);
        if (playtest)
        {
            MemoryGrove.Populate(server.World, new GameServerSettings().SpawnPoint);
            return $"Memory Grove with {MemoryGrove.CreatureCount} creatures";
        }

        server.Populate("mossback", DevGrazers, DevGrazerHerds);
        server.Populate("bogfox", DevPredators, DevPredatorPacks, firstGroup: DevGrazerHerds);
        return $"{DevGrazers + DevPredators} creatures";
    }

    /// <summary>Creature world settings on the dev ground with the given path finding.</summary>
    public static CreatureWorldSettings Creatures(INavigation navigation) => new() { Ground = Ground, Navigation = navigation };
}
