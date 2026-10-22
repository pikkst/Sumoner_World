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
/// <param name="Skills">Learned skills and spent points (M2-10); null in saves from before they existed.</param>
/// <param name="Combos">Learned combos and landings (M2-12); null in saves from before they existed.</param>
/// <param name="Equipped">Worn item ids by position (M2-14); null in older saves.</param>
/// <param name="Bag">Its own bag, slot by slot (M2-14b); null when it has none.</param>
public sealed record CreatureSave(
    StableId Id,
    string Species,
    Vector3 Position,
    Vector3 Facing,
    int Group,
    MindState Mind,
    BondState Bond,
    Progression.CreatureGrowth? Growth = null,
    Progression.CreatureSkills? Skills = null,
    Combat.CreatureCombos? Combos = null,
    IReadOnlyList<string>? Equipped = null,
    IReadOnlyList<SavedItem>? Bag = null);

/// <summary>One saved reputation score.</summary>
/// <param name="Species">Species data id.</param>
/// <param name="Region">Region index.</param>
/// <param name="Player">Account id.</param>
/// <param name="Score">Score.</param>
public readonly record struct ReputationSave(string Species, int Region, ulong Player, int Score);

/// <summary>A player's saved place in the world and progress (inventories arrive with gameplay).</summary>
/// <param name="Account">Account id.</param>
/// <param name="Position">Where the avatar was last.</param>
/// <param name="Progress">Class, rank, level and experience (M2-09); null in saves from before it existed (a new player's).</param>
/// <param name="Inventory">Gold and items (M2-13); null in saves from before it existed (empty).</param>
/// <param name="Equipped">Worn item ids by position (M2-14); empty strings for empty positions; null in older saves.</param>
/// <param name="Quests">Taken and done quests by id (M2-19); null in older saves.</param>
public readonly record struct PlayerSave(
    ulong Account,
    Vector3 Position,
    Progression.PlayerProgress? Progress = null,
    InventorySave? Inventory = null,
    IReadOnlyList<string>? Equipped = null,
    IReadOnlyList<Quests.QuestSave>? Quests = null)
{
    /// <summary>Equal when every field is, the worn ids compared position by position (a missing position is empty).</summary>
    public bool Equals(PlayerSave other) =>
        Account == other.Account && Position == other.Position && Progress == other.Progress && Equals(Inventory, other.Inventory)
        && SameWorn(Equipped, other.Equipped) && (Quests ?? []).SequenceEqual(other.Quests ?? []);

    private static bool SameWorn(IReadOnlyList<string>? a, IReadOnlyList<string>? b)
    {
        int count = Math.Max(a?.Count ?? 0, b?.Count ?? 0);
        for (int i = 0; i < count; i++)
        {
            string left = a is not null && i < a.Count ? a[i] : string.Empty;
            string right = b is not null && i < b.Count ? b[i] : string.Empty;
            if (left != right)
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Account, Position);
}

/// <summary>A saved inventory: gold and the slots by item id (so a reordered items file cannot mix them up).</summary>
/// <param name="Gold">Gold.</param>
/// <param name="Items">Non-empty slots in slot order.</param>
public sealed record InventorySave(int Gold, IReadOnlyList<SavedItem> Items)
{
    /// <summary>Equal when gold and every slot are.</summary>
    public bool Equals(InventorySave? other) => other is not null && Gold == other.Gold && Items.SequenceEqual(other.Items);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Gold, Items.Count);
}

/// <summary>A saved slot.</summary>
/// <param name="Item">Item id.</param>
/// <param name="Count">How many.</param>
public readonly record struct SavedItem(string Item, int Count);
