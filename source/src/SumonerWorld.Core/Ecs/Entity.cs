namespace SumonerWorld.Core.Ecs;

/// <summary>
/// Handle to an entity: slot index plus generation (ADR-002). A handle kept after the entity is destroyed never reaches the
/// entity that later reuses the slot. Handles are runtime-only: never saved or sent over the network (use <see cref="StableId"/>).
/// </summary>
/// <param name="Index">Slot in the world, in [0, capacity).</param>
/// <param name="Generation">Incremented every time the slot is freed.</param>
public readonly record struct Entity(int Index, int Generation);
