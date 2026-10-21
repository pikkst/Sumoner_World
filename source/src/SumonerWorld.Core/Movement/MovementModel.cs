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

    /// <summary>Highest corner of the walkable area (X and Z; height comes from <see cref="Ground"/>).</summary>
    public Vector3 BoundsMax { get; init; } = new(512, 0, 512);

    /// <summary>The terrain characters stand on (TDD §5.1); null = flat at height 0. Server and client must use the same one.</summary>
    public World.Heightfield? Ground { get; init; }
}

/// <summary>
/// Character movement shared by the server and client prediction (TDD §3.2): both run exactly this code, one input per tick, so a
/// client that applies the same inputs from the same position predicts the server's result. Terrain and collision arrive with M1-20.
/// </summary>
public interface IMovementModel
{
    /// <summary>Position after applying <paramref name="input"/> for one tick.</summary>
    Vector3 Advance(Vector3 position, MoveInput input);

    /// <summary>Position after one tick of a dash along a unit direction at a percentage of walking speed (a dodge).</summary>
    Vector3 Dash(Vector3 position, Vector3 direction, int speedPercent);
}

/// <summary>Movement inside the region bounds on the terrain (M1-15; heights since M1-20). Collision with props comes later.</summary>
public sealed class MovementModel : IMovementModel
{
    private readonly float _speed;
    private readonly Vector3 _min;
    private readonly Vector3 _max;
    private readonly World.Heightfield? _ground;

    /// <summary>Creates the model.</summary>
    public MovementModel(MovementSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(settings.SpeedPerTick);
        _speed = settings.SpeedPerTick;
        _max = settings.BoundsMax with { Y = float.MaxValue };
        _min = settings.BoundsMin with { Y = float.MinValue };
        _ground = settings.Ground;
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
    public Vector3 Advance(Vector3 position, MoveInput input) => Step(position, Direction(input) * _speed);

    /// <inheritdoc />
    public Vector3 Dash(Vector3 position, Vector3 direction, int speedPercent) => Step(position, direction * (_speed * speedPercent / 100f));

    private Vector3 Step(Vector3 position, Vector3 offset)
    {
        Vector3 next = Vector3.Clamp(position + offset, _min, _max);
        return next with { Y = _ground?.HeightAt(next.X, next.Z) ?? 0f };
    }
}
