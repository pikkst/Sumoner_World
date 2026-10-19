using System.Numerics;
using SumonerWorld.Core.Movement;

namespace SumonerWorld.Tests.Core.Movement;

public class MovementModelTests
{
    private static readonly MovementModel Model = new(new MovementSettings { SpeedPerTick = 0.2f, BoundsMax = new Vector3(100, 0, 100) });

    [Fact]
    public void Full_input_moves_one_tick_of_speed()
    {
        Vector3 next = Model.Advance(new Vector3(50, 0, 50), new MoveInput(1000, 0));

        Assert.Equal(new Vector3(50.2f, 0, 50), next);
    }

    [Fact]
    public void Diagonals_are_not_faster_and_small_inputs_are_slower()
    {
        Vector3 diagonal = Model.Advance(new Vector3(50, 0, 50), new MoveInput(1000, 1000)) - new Vector3(50, 0, 50);
        Vector3 half = Model.Advance(new Vector3(50, 0, 50), new MoveInput(0, -500)) - new Vector3(50, 0, 50);

        Assert.Equal(0.2f, diagonal.Length(), 4);
        Assert.Equal(0.1f, half.Length(), 4);
        Assert.Equal(Vector3.Zero, Model.Advance(Vector3.Zero, MoveInput.None));
    }

    [Fact]
    public void Movement_stays_inside_the_bounds_and_out_of_range_axes_are_clamped()
    {
        Assert.Equal(new Vector3(100, 0, 0), Model.Advance(new Vector3(99.9f, 0, 0), new MoveInput(1000, -1000)) with { Z = 0 });
        Assert.Equal(MovementModel.Direction(new MoveInput(1000, 0)), MovementModel.Direction(new MoveInput(short.MaxValue, 0)));
    }

    [Fact]
    public void The_same_inputs_from_the_same_position_give_the_same_path()
    {
        Vector3 a = new(10, 0, 10);
        Vector3 b = a;
        for (int i = 0; i < 500; i++)
        {
            var input = new MoveInput((short)((i * 37 % 2001) - 1000), (short)((i * 91 % 2001) - 1000));
            a = Model.Advance(a, input);
            b = Model.Advance(b, input);
        }

        Assert.Equal(a, b);
    }
}
