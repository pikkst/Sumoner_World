using System.Numerics;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Ecs;

namespace SumonerWorld.Scenarios.B5;

/// <summary>World position in metres.</summary>
public record struct Position(Vector3 Value);

/// <summary>Velocity in metres per tick.</summary>
public record struct Velocity(Vector3 Value);

/// <summary>Ticks until the creature picks a new heading.</summary>
public record struct WanderTimer(int TicksLeft);

/// <summary>The emote the creature currently shows (replicated to clients later, AI Bible §6.2).</summary>
public record struct CurrentEmote(Emote Value);

/// <summary>The creature's mind in the mind store.</summary>
public record struct MindRef(MindHandle Handle);

/// <summary>Published by perception: how many creatures this one can sense.</summary>
public readonly record struct NeighbourCount(Entity Creature, int Count);
