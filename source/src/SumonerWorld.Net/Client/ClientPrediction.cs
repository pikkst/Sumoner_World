using System.Numerics;
using SumonerWorld.Core.Movement;
using SumonerWorld.Net.Messages;

namespace SumonerWorld.Net.Client;

/// <summary>What a reconciliation did to the predicted position.</summary>
public enum Correction : byte
{
    /// <summary>The prediction was exact.</summary>
    None,

    /// <summary>Error up to 0.5 m: corrected at once.</summary>
    Snapped,

    /// <summary>Error 0.5–3 m: corrected, and the visible position catches up over 100 ms.</summary>
    Smoothed,

    /// <summary>Error above 3 m: teleported.</summary>
    Teleported,
}

/// <summary>
/// Own-character prediction and reconciliation (TDD §3.2): the client applies its inputs at once with the same
/// <see cref="IMovementModel"/> the server uses, and keeps the inputs the server has not acknowledged yet. On each snapshot it
/// restarts from the server's position, replays those inputs and compares the result with what it predicted.
/// </summary>
public interface IClientPrediction
{
    /// <summary>True once the first snapshot set the starting position.</summary>
    bool IsInitialized { get; }

    /// <summary>The predicted (simulation) position.</summary>
    Vector3 Position { get; }

    /// <summary>Where to draw the character: the position plus a correction offset that fades out.</summary>
    Vector3 RenderPosition { get; }

    /// <summary>Applies one input locally and returns it with its sequence, ready to send.</summary>
    PlayerInput Record(MoveInput input, byte buttons);

    /// <summary>Reconciles with the server's position after input <paramref name="ackedInput"/>.</summary>
    Correction Reconcile(uint ackedInput, Vector3 serverPosition);
}

/// <summary>Prediction with a ring of up to 64 unacknowledged inputs (≈ 2 s at 30 Hz); older ones are dropped.</summary>
public sealed class ClientPrediction : IClientPrediction
{
    /// <summary>Unacknowledged inputs kept.</summary>
    public const int Capacity = 64;

    /// <summary>Errors up to this are corrected at once (metres).</summary>
    public const float SnapThreshold = 0.5f;

    /// <summary>Errors above this teleport (metres).</summary>
    public const float TeleportThreshold = 3f;

    /// <summary>Ticks over which a smoothed correction fades (100 ms at 30 Hz).</summary>
    public const int SmoothTicks = 3;

    private readonly IMovementModel _movement;
    private readonly PlayerInput[] _pending = new PlayerInput[Capacity];
    private int _start;
    private Vector3 _offset;
    private int _smoothLeft;

    /// <summary>Creates the prediction.</summary>
    public ClientPrediction(IMovementModel movement)
    {
        ArgumentNullException.ThrowIfNull(movement);
        _movement = movement;
    }

    /// <inheritdoc />
    public bool IsInitialized { get; private set; }

    /// <inheritdoc />
    public Vector3 Position { get; private set; }

    /// <inheritdoc />
    public Vector3 RenderPosition => Position + _offset;

    /// <summary>Sequence of the last recorded input.</summary>
    public uint LastSequence { get; private set; }

    /// <summary>Last input sequence the server acknowledged.</summary>
    public uint AckedSequence { get; private set; }

    /// <summary>Inputs not acknowledged yet.</summary>
    public int PendingCount { get; private set; }

    /// <summary>Reconciliations by result since the start (index = <see cref="Correction"/>).</summary>
    public IReadOnlyList<int> Corrections => _corrections;

    private readonly int[] _corrections = new int[4];

    /// <inheritdoc />
    public PlayerInput Record(MoveInput input, byte buttons)
    {
        if (!IsInitialized)
        {
            throw new InvalidOperationException("Wait for the first snapshot before predicting.");
        }

        var recorded = new PlayerInput(++LastSequence, input.X, input.Z, buttons);
        if (PendingCount == Capacity)
        {
            _start = (_start + 1) % Capacity;
            PendingCount--;
        }

        _pending[(_start + PendingCount) % Capacity] = recorded;
        PendingCount++;
        Position = _movement.Advance(Position, input);
        FadeOffset();
        return recorded;
    }

    /// <summary>Forgets the prediction (the connection changed); the next snapshot starts it again.</summary>
    public void Reset()
    {
        IsInitialized = false;
        PendingCount = 0;
        _start = 0;
        _offset = Vector3.Zero;
        _smoothLeft = 0;
    }

    /// <summary>Copies the newest unacknowledged inputs (at most <paramref name="destination"/>.Length), oldest first.</summary>
    public int CopyNewest(Span<PlayerInput> destination)
    {
        int count = Math.Min(destination.Length, PendingCount);
        for (int i = 0; i < count; i++)
        {
            destination[i] = _pending[(_start + PendingCount - count + i) % Capacity];
        }

        return count;
    }

    /// <inheritdoc />
    public Correction Reconcile(uint ackedInput, Vector3 serverPosition)
    {
        if (!IsInitialized)
        {
            // Continue right after the server's last applied input: everything later was never applied.
            IsInitialized = true;
            Position = serverPosition;
            AckedSequence = ackedInput;
            LastSequence = ackedInput;
            return Correction.None;
        }

        if (ackedInput < AckedSequence)
        {
            return Correction.None;
        }

        AckedSequence = ackedInput;
        while (PendingCount > 0 && _pending[_start].Sequence <= ackedInput)
        {
            _start = (_start + 1) % Capacity;
            PendingCount--;
        }

        Vector3 replayed = serverPosition;
        for (int i = 0; i < PendingCount; i++)
        {
            PlayerInput input = _pending[(_start + i) % Capacity];
            replayed = _movement.Advance(replayed, new MoveInput(input.MoveX, input.MoveZ));
        }

        Correction correction = Classify(Vector3.Distance(replayed, Position));
        if (correction == Correction.Smoothed)
        {
            _offset += Position - replayed;
            _smoothLeft = SmoothTicks;
        }
        else if (correction == Correction.Teleported)
        {
            _offset = Vector3.Zero;
            _smoothLeft = 0;
        }

        Position = replayed;
        _corrections[(int)correction]++;
        return correction;
    }

    private static Correction Classify(float error) => error switch
    {
        0f => Correction.None,
        <= SnapThreshold => Correction.Snapped,
        <= TeleportThreshold => Correction.Smoothed,
        _ => Correction.Teleported,
    };

    private void FadeOffset()
    {
        if (_smoothLeft > 0)
        {
            _offset -= _offset / _smoothLeft;
            _smoothLeft--;
        }
    }
}
