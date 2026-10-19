using SumonerWorld.Core.Simulation;

namespace SumonerWorld.Tests.Core.Simulation;

public class FixedStepAccumulatorTests
{
    private const long NanosecondsPerSecond = 1_000_000_000L;

    [Fact]
    public void Uneven_frames_adding_up_to_one_second_give_exactly_one_second_of_ticks()
    {
        var accumulator = new FixedStepAccumulator(SimulationSettings.Default);
        long[] frames = [7_000_000, 16_666_667, 3_333_333, 25_000_000, 1_000_000];
        long elapsed = 0;
        int ticks = 0;

        for (int i = 0; elapsed < NanosecondsPerSecond; i++)
        {
            long frame = Math.Min(frames[i % frames.Length], NanosecondsPerSecond - elapsed);
            elapsed += frame;
            ticks += accumulator.Accumulate(frame);
        }

        Assert.Equal(SimulationSettings.DefaultTicksPerSecond, ticks);
        Assert.Equal(0, accumulator.DroppedTicks);
    }

    [Fact]
    public void One_hour_of_60_fps_frames_does_not_drift()
    {
        // 60 frames of 16,666,667 ns overshoot a second by 20 ns; over an hour that is 72 µs, far below one tick.
        const long frameNanoseconds = 16_666_667;
        const int framesPerHour = 60 * 60 * 60;
        const long ticksPerHour = 60L * 60 * SimulationSettings.DefaultTicksPerSecond;
        var accumulator = new FixedStepAccumulator(SimulationSettings.Default);
        long ticks = 0;

        for (int i = 0; i < framesPerHour; i++)
        {
            ticks += accumulator.Accumulate(frameNanoseconds);
        }

        Assert.Equal(ticksPerHour, ticks);
    }

    [Fact]
    public void A_long_hitch_is_capped_and_the_dropped_ticks_are_counted()
    {
        var accumulator = new FixedStepAccumulator(SimulationSettings.Default);

        int ticks = accumulator.Accumulate(NanosecondsPerSecond);

        Assert.Equal(SimulationSettings.DefaultMaxCatchUpTicks, ticks);
        Assert.Equal(SimulationSettings.DefaultTicksPerSecond - SimulationSettings.DefaultMaxCatchUpTicks, accumulator.DroppedTicks);
    }

    [Fact]
    public void Alpha_reports_progress_towards_the_next_tick()
    {
        var accumulator = new FixedStepAccumulator(SimulationSettings.Default);
        long halfTick = NanosecondsPerSecond / SimulationSettings.DefaultTicksPerSecond / 2;

        int ticks = accumulator.Accumulate(halfTick);

        Assert.Equal(0, ticks);
        Assert.InRange(accumulator.Alpha, 0.49, 0.51);
    }

    [Fact]
    public void Negative_elapsed_time_is_rejected()
    {
        var accumulator = new FixedStepAccumulator(SimulationSettings.Default);

        Assert.Throws<ArgumentOutOfRangeException>(() => accumulator.Accumulate(-1));
    }
}
