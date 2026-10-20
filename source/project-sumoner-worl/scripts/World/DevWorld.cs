using SumonerWorld.Core.Movement;
using SumonerWorld.Core.Navigation;
using SumonerWorld.Core.World;

namespace SumonerWorld.Game.World;

/// <summary>
/// The M1 dev world every host builds the same way (TDD §5.1): the <see cref="DevRegion"/> ground, created once per process and shared
/// (it is read-only), and the movement settings on it. The server, the client's prediction and the bots must stand on the same ground,
/// or prediction would be corrected every tick.
/// </summary>
public static class DevWorld
{
    private static readonly Lazy<Heightfield> SharedGround = new(() => DevRegion.CreateGround());

    /// <summary>The 512 m dev region's ground.</summary>
    public static Heightfield Ground => SharedGround.Value;

    /// <summary>Player movement on the dev ground (server and clients).</summary>
    public static MovementSettings Movement() => new() { Ground = Ground };

    /// <summary>Creature world settings on the dev ground with the given path finding.</summary>
    public static CreatureWorldSettings Creatures(INavigation navigation) => new() { Ground = Ground, Navigation = navigation };
}
