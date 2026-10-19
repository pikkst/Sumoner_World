using System.Numerics;

namespace SumonerWorld.Core.Movement;

/// <summary>Desired movement for one tick, as sent by the client (TDD §3.2): each axis −1000…1000, never a position (R31).</summary>
/// <param name="X">Desired movement along X.</param>
/// <param name="Z">Desired movement along Z.</param>
public readonly record struct MoveInput(short X, short Z)
{
    /// <summary>Largest magnitude per axis.</summary>
    public const short MaxAxis = 1000;

    /// <summary>No movement.</summary>
    public static MoveInput None => default;
}

/// <summary>Settings of the shared movement model.</summary>
public sealed class MovementSettings
{
    /// <summary>Distance covered per tick at full input (default 0.2 m = 6 m/s at 30 Hz).</summary>
    public float SpeedPerTick { get; init; } = 0.2f;

    /// <summary>Lowest corner of the walkable area.</summary>
    public Vector3 BoundsMin { get; init; }

    /// <summary>Highest corner of the walkable area.</summary>
    public Vector3 BoundsMax { get; init; } = new(512, 0, 512);
}

/// <summary>
/// Character movement shared by the server and client prediction (TDD §3.2): both run exactly this code, one input per tick, so a
/// client that applies the same inputs from the same position predicts the server's result. Terrain and collision arrive with M1-20.
/// </summary>
public interface IMovementModel
{
    /// <summary>Position after applying <paramref name="input"/> for one tick.</summary>
    Vector3 Advance(Vector3 position, MoveInput input);
}

/// <summary>Flat movement inside the region bounds (M1-15).</summary>
public sealed class MovementModel : IMovementModel
{
    private readonly float _speed;
    private readonly Vector3 _min;
    private readonly Vector3 _max;

    /// <summary>Creates the model.</summary>
    public MovementModel(MovementSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(settings.SpeedPerTick);
        _speed = settings.SpeedPerTick;
        _min = settings.BoundsMin;
        _max = settings.BoundsMax;
    }

    /// <summary>Unit-or-shorter direction of an input; axes outside ±1000 are clamped and diagonals never move faster.</summary>
    public static Vector3 Direction(MoveInput input)
    {
        var direction = new Vector3(
            Math.Clamp((int)input.X, -MoveInput.MaxAxis, MoveInput.MaxAxis) / (float)MoveInput.MaxAxis,
            0,
            Math.Clamp((int)input.Z, -MoveInput.MaxAxis, MoveInput.MaxAxis) / (float)MoveInput.MaxAxis);
        float lengthSquared = direction.LengthSquared();
        return lengthSquared > 1f ? direction / MathF.Sqrt(lengthSquared) : direction;
    }

    /// <inheritdoc />
    public Vector3 Advance(Vector3 position, MoveInput input) => Vector3.Clamp(position + (Direction(input) * _speed), _min, _max);
}
