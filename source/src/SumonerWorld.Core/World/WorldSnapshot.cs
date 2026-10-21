using System.Numerics;
using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Ecs;

namespace SumonerWorld.Core.World;

/// <summary>
/// The saved state of a <see cref="CreatureWorld"/> (TDD §4.2, M1-17b): clock, random state, id counter, every creature and every
/// reputation score. Species are named by their stable data id, never by index. Transient state (AI tier, focus, goal, emote,
/// running behaviour trees) is not saved: it rebuilds within a second.
/// </summary>
/// <param name="Tick">World tick.</param>
/// <param name="RandomState">State of the world's random generator.</param>
/// <param name="LastStableId">Last stable id issued.</param>
/// <param name="Creatures">Every creature, by stable id.</param>
/// <param name="Reputation">Every reputation score.</param>
public sealed record WorldSnapshot(
    ulong Tick, ulong RandomState, ulong LastStableId, IReadOnlyList<CreatureSave> Creatures, IReadOnlyList<ReputationSave> Reputation);

/// <summary>One saved creature.</summary>
/// <param name="Id">Stable id.</param>
/// <param name="Species">Species data id.</param>
/// <param name="Position">Position.</param>
/// <param name="Facing">Facing direction.</param>
/// <param name="Group">Herd.</param>
/// <param name="Mind">Mind state.</param>
/// <param name="Bond">Bond with its owner (wild when unowned).</param>
/// <param name="Growth">Tier, level, stage and experience (M2-09); null in saves from before it existed.</param>
public sealed record CreatureSave(
    StableId Id, string Species, Vector3 Position, Vector3 Facing, int Group, MindState Mind, BondState Bond, Progression.CreatureGrowth? Growth = null);

/// <summary>One saved reputation score.</summary>
/// <param name="Species">Species data id.</param>
/// <param name="Region">Region index.</param>
/// <param name="Player">Account id.</param>
/// <param name="Score">Score.</param>
public readonly record struct ReputationSave(string Species, int Region, ulong Player, int Score);

/// <summary>A player's saved place in the world (characters, inventories and stats arrive with gameplay).</summary>
/// <param name="Account">Account id.</param>
/// <param name="Position">Where the avatar was last.</param>
public readonly record struct PlayerSave(ulong Account, Vector3 Position);
