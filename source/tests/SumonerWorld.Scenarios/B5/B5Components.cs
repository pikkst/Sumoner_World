using System.Numerics;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Ecs;

namespace SumonerWorld.Scenarios.B5;

/// <summary>World position in metres.</summary>
public record struct Position(Vector3 Value);

/// <summary>Where the creature is walking to (from a MoveTo intent); <c>Active</c> is 1 while moving, 0 when stopped.</summary>
public record struct MoveTarget(Vector3 Target, float Speed, int Active);

/// <summary>The emote the creature currently shows (replicated to clients later, AI Bible §6.2).</summary>
public record struct CurrentEmote(Emote Value);

/// <summary>The goal the utility AI chose (0 = idle), AI Bible §7.</summary>
public record struct CurrentGoal(ushort Goal);

/// <summary>The creature's mind in the mind store.</summary>
public record struct MindRef(MindHandle Handle);

/// <summary>Published by perception: how many creatures this one can sense.</summary>
public readonly record struct NeighbourCount(Entity Creature, int Count);
