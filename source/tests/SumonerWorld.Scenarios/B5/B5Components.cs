namespace SumonerWorld.Scenarios.B5;

/// <summary>A stand-in player's position on its fixed patrol route (B5 has no real players yet).</summary>
/// <param name="Waypoint">Index of the waypoint the player walks to.</param>
public record struct PatrolRoute(int Waypoint);
