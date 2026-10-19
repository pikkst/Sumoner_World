namespace SumonerWorld.Core.Ecs;

/// <summary>Monotonic <see cref="IStableIdAllocator"/>: ids count up from the saved high-water mark (deterministic, R11).</summary>
public sealed class StableIdAllocator : IStableIdAllocator
{
    /// <summary>Creates an allocator for a new world (<paramref name="lastIssued"/> = 0) or a loaded one (the saved value).</summary>
    public StableIdAllocator(ulong lastIssued = 0)
    {
        LastIssued = lastIssued;
    }

    /// <inheritdoc />
    public ulong LastIssued { get; private set; }

    /// <inheritdoc />
    public StableId Allocate()
    {
        if (LastIssued == ulong.MaxValue)
        {
            throw new InvalidOperationException("Stable id space exhausted for this world.");
        }

        LastIssued++;
        return new StableId(LastIssued);
    }
}
