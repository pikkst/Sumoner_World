using SumonerWorld.Net.Messages;

namespace SumonerWorld.Net.Server;

/// <summary>
/// One player's received inputs, by sequence (TDD §3.2). The server applies each input at most once, in sequence order, one per tick
/// (two while catching up). Copies of applied inputs are ignored. When an input never arrives (every redundant copy lost), the next
/// one is applied; an input more than <see cref="Capacity"/> ahead (after a long outage) skips everything before it. Either way the
/// client's reconciliation corrects the difference, and skipping only ever costs the client movement.
/// </summary>
internal sealed class PlayerInputBuffer
{
    /// <summary>Inputs that can wait (≈ 1 s).</summary>
    public const int Capacity = 32;

    /// <summary>With more inputs waiting than this, two are applied per tick.</summary>
    public const int CatchUpThreshold = 4;

    private readonly PlayerInput[] _inputs = new PlayerInput[Capacity];
    private readonly bool[] _filled = new bool[Capacity];

    /// <summary>The last applied sequence (0 before the first).</summary>
    public uint LastApplied { get; private set; }

    /// <summary>Inputs waiting.</summary>
    public int Buffered { get; private set; }

    /// <summary>Stores an input unless it was applied already or is too far ahead.</summary>
    public void Offer(in PlayerInput input)
    {
        if (input.Sequence <= LastApplied)
        {
            return;
        }

        if (input.Sequence - LastApplied > Capacity)
        {
            Reset();
            LastApplied = input.Sequence - 1;
        }

        int slot = (int)(input.Sequence % Capacity);
        if (!_filled[slot])
        {
            _filled[slot] = true;
            _inputs[slot] = input;
            Buffered++;
        }
    }

    /// <summary>Forgets waiting inputs (the connection changed); <see cref="LastApplied"/> stays.</summary>
    public void Reset()
    {
        Array.Clear(_filled);
        Buffered = 0;
    }

    /// <summary>Takes the lowest waiting input.</summary>
    public bool TryTakeNext(out PlayerInput input)
    {
        for (uint sequence = LastApplied + 1; Buffered > 0 && sequence <= LastApplied + Capacity; sequence++)
        {
            int slot = (int)(sequence % Capacity);
            if (_filled[slot] && _inputs[slot].Sequence == sequence)
            {
                input = _inputs[slot];
                _filled[slot] = false;
                Buffered--;
                LastApplied = sequence;
                return true;
            }
        }

        input = default;
        return false;
    }
}
