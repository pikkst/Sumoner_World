namespace SumonerWorld.Core.Ecs;

/// <summary>Issues <see cref="StableId"/>s for one world. Server-only (R30).</summary>
public interface IStableIdAllocator
{
    /// <summary>The last id issued; saved with the world so ids are never reused after a restart.</summary>
    ulong LastIssued { get; }

    /// <summary>Issues the next id.</summary>
    StableId Allocate();
}
