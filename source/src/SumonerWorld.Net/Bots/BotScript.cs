using System.Numerics;
using SumonerWorld.Core.Movement;
using SumonerWorld.Core.Randomness;

namespace SumonerWorld.Net.Bots;

/// <summary>
/// A bot's recorded movement (B2: "bot clients replaying recorded inputs"): one input per tick, generated once from a seed, so every
/// run of the benchmark sends the same inputs. Walks of 1–5 s in one of eight directions with pauses in between, recorded against
/// the movement model so the walk stays within <c>leash</c> metres of where the bot started. Longer runs repeat the recording.
/// </summary>
public sealed class BotScript
{
    private const int MinSegmentTicks = 30;
    private const int MaxSegmentTicks = 150;
    private const int IdlePercent = 20;
    private const int Directions = 8;

    private readonly MoveInput[] _inputs;

    private BotScript(MoveInput[] inputs)
    {
        _inputs = inputs;
    }

    /// <summary>Recorded ticks before the recording repeats.</summary>
    public int Length => _inputs.Length;

    /// <summary>Records <paramref name="ticks"/> inputs.</summary>
    public static BotScript Record(ulong seed, int ticks, MovementSettings movement, float leash)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ticks);
        ArgumentNullException.ThrowIfNull(movement);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(leash);

        var random = new SeededRandom(seed);
        var inputs = new MoveInput[ticks];
        Vector3 offset = Vector3.Zero;
        int tick = 0;
        while (tick < ticks)
        {
            int length = random.NextInt(MinSegmentTicks, MaxSegmentTicks + 1);
            MoveInput input = random.NextInt(0, 100) < IdlePercent ? MoveInput.None : Heading(random.NextInt(0, Directions));
            for (int i = 0; i < length && tick < ticks; i++, tick++)
            {
                Vector3 step = MovementModel.Direction(input) * movement.SpeedPerTick;
                if (offset.LengthSquared() > leash * leash && Vector3.Dot(step, offset) > 0)
                {
                    input = Toward(-offset);
                    step = MovementModel.Direction(input) * movement.SpeedPerTick;
                }

                inputs[tick] = input;
                offset += step;
            }
        }

        return new BotScript(inputs);
    }

    /// <summary>The input for a tick.</summary>
    public MoveInput InputAt(long tick) => _inputs[(int)(tick % _inputs.Length)];

    private static MoveInput Heading(int index)
    {
        float angle = index * MathF.Tau / Directions;
        return new MoveInput((short)MathF.Round(MathF.Sin(angle) * MoveInput.MaxAxis), (short)MathF.Round(MathF.Cos(angle) * MoveInput.MaxAxis));
    }

    private static MoveInput Toward(Vector3 direction)
    {
        float angle = MathF.Atan2(direction.X, direction.Z);
        int index = (int)MathF.Round(angle / (MathF.Tau / Directions));
        return Heading(((index % Directions) + Directions) % Directions);
    }
}
