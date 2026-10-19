using System.Numerics;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Cognition.Lod;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Ecs;

namespace SumonerWorld.Core.World;

/// <summary>World position in metres.</summary>
/// <param name="Value">The position.</param>
public record struct Position(Vector3 Value);

/// <summary>Direction the body faces (ground plane); the view cone points this way.</summary>
/// <param name="Value">Unit direction.</param>
public record struct Facing(Vector3 Value);

/// <summary>Where the entity is walking to; <c>Active</c> is 1 while moving, 0 when stopped (int, so the struct has no padding).</summary>
/// <param name="Target">Destination.</param>
/// <param name="Speed">Metres per tick.</param>
/// <param name="Active">1 while moving.</param>
public record struct MoveTarget(Vector3 Target, float Speed, int Active);

/// <summary>The emote the creature currently shows (replicated to clients, AI Bible §6.2).</summary>
/// <param name="Value">The emote.</param>
public record struct CurrentEmote(Emote Value);

/// <summary>The goal the utility AI chose (0 = idle), AI Bible §7.</summary>
/// <param name="Goal">Goal id.</param>
public record struct CurrentGoal(ushort Goal);

/// <summary>The creature's AI level of detail (AI Bible §9.1).</summary>
/// <param name="Tier">The tier.</param>
public record struct AiTier(AiLodTier Tier);

/// <summary>What the creature pays attention to; <c>Has</c> is 1 when there is a focus, <c>Distance</c> is 0–1000.</summary>
/// <param name="Entity">The focused entity.</param>
/// <param name="Distance">Normalised distance.</param>
/// <param name="Has">1 when focused.</param>
public record struct FocusRef(Entity Entity, int Distance, int Has);

/// <summary>The creature's mind in the mind store.</summary>
/// <param name="Handle">The mind.</param>
public record struct MindRef(MindHandle Handle);

/// <summary>The social group (herd, pack) a creature belongs to; rumours spread between group members (AI Bible §4.5).</summary>
/// <param name="Group">Group id.</param>
public record struct GroupRef(int Group);

/// <summary>The creature's species, as an index into the data registry's species list.</summary>
/// <param name="Index">Species index.</param>
public record struct SpeciesRef(ushort Index);

/// <summary>A player's avatar; <c>AccountId</c> is what creatures remember them by.</summary>
/// <param name="AccountId">Player account id.</param>
public record struct PlayerAvatar(ulong AccountId);

/// <summary>Published by perception: how many creatures a creature senses (crowding, AI Bible §6.1).</summary>
/// <param name="Creature">The sensing creature.</param>
/// <param name="Count">Creatures sensed.</param>
public readonly record struct NeighboursSensed(Entity Creature, int Count);
