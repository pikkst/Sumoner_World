using System.Runtime.InteropServices;

namespace SumonerWorld.Core.Diagnostics;

/// <summary>
/// Incremental 64-bit FNV-1a hash of simulation state, for determinism checks and replays (TDD §2.3). Feed state in a fixed
/// order; two runs are identical when every tick's hash matches. Only hash structs without padding (their bytes are their value).
/// </summary>
public struct StateHash
{
    private const ulong OffsetBasis = 0xCBF29CE484222325UL;
    private const ulong Prime = 0x100000001B3UL;

    private ulong _value;
    private bool _started;

    /// <summary>The hash of everything added so far.</summary>
    public readonly ulong Value => _started ? _value : OffsetBasis;

    /// <summary>Adds the raw bytes of a span of unmanaged values.</summary>
    public void Add<T>(ReadOnlySpan<T> values)
        where T : unmanaged
    {
        ulong hash = Value;
        foreach (byte b in MemoryMarshal.AsBytes(values))
        {
            hash = (hash ^ b) * Prime;
        }

        _value = hash;
        _started = true;
    }

    /// <summary>Adds one 64-bit value.</summary>
    public void Add(ulong value) => Add<ulong>(new ReadOnlySpan<ulong>(in value));
}
