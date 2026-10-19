namespace SumonerWorld.Core.Ecs;

/// <summary>
/// Server-generated 64-bit id for anything that persists or is referenced across the network, saves and memories (TDD §2.2).
/// Stored as a component on the entity. Value 0 means "none".
/// </summary>
/// <param name="Value">The id; never reused within a world.</param>
public readonly record struct StableId(ulong Value)
{
    /// <summary>The empty id.</summary>
    public static StableId None => default;

    /// <summary>True when this is not <see cref="None"/>.</summary>
    public bool IsAssigned => Value != 0;
}
