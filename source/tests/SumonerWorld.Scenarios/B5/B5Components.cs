using System.Numerics;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Cognition.Lod;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Ecs;

namespace SumonerWorld.Scenarios.B5;

/// <summary>World position in metres.</summary>
public record struct Position(Vector3 Value);

/// <summary>Direction the body faces (ground plane); the view cone points this way.</summary>
public record struct Facing(Vector3 Value);

/// <summary>Where the entity is walking to (from a MoveTo intent); <c>Active</c> is 1 while moving, 0 when stopped.</summary>
public record struct MoveTarget(Vector3 Target, float Speed, int Active);

/// <summary>The emote the creature currently shows (replicated to clients later, AI Bible §6.2).</summary>
public record struct CurrentEmote(Emote Value);

/// <summary>The goal the utility AI chose (0 = idle), AI Bible §7.</summary>
public record struct CurrentGoal(ushort Goal);

/// <summary>The creature's AI level of detail (AI Bible §9.1).</summary>
public record struct AiTier(AiLodTier Tier);

/// <summary>What the creature pays attention to; <c>Has</c> is 1 when there is a focus, <c>Distance</c> is 0–1000.</summary>
public record struct FocusRef(Entity Entity, int Distance, int Has);

/// <summary>The creature's mind in the mind store.</summary>
public record struct MindRef(MindHandle Handle);

/// <summary>The herd a creature belongs to (rumours spread between herd-mates, AI Bible §4.5).</summary>
public record struct GroupRef(int Group);

/// <summary>Marks a stand-in player (B5 has no network yet); <c>AccountId</c> is what creatures remember them by.</summary>
public record struct PlayerTag(ulong AccountId, int Waypoint);

/// <summary>Published by perception: how many creatures this one can sense.</summary>
public readonly record struct NeighbourCount(Entity Creature, int Count);
