namespace SumonerWorld.Core.Simulation;

/// <summary>Snapshot of simulation time handed to every system for one tick.</summary>
/// <param name="Index">World tick counter since world creation (saved with the world).</param>
/// <param name="Day">In-game day number, starting at 0.</param>
/// <param name="TickOfDay">Tick within the current in-game day, in [0, ticks per day).</param>
public readonly record struct SimulationTick(ulong Index, ulong Day, int TickOfDay);
