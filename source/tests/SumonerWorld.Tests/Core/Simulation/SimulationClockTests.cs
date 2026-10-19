using SumonerWorld.Core.Simulation;

namespace SumonerWorld.Tests.Core.Simulation;

public class SimulationClockTests
{
    private const int TicksPerSecond = 30;
    private const int TicksPerDay = 10;
    private const int MaxCatchUpTicks = 5;

    private static readonly SimulationSettings SmallDay = new(TicksPerSecond, TicksPerDay, MaxCatchUpTicks);

    [Fact]
    public void Default_settings_match_the_tdd()
    {
        // TDD §2.1: 30 Hz, one in-game day = 48 real minutes.
        var settings = SimulationSettings.Default;
        const int secondsPerMinute = 60;
        const int realMinutesPerDay = 48;

        Assert.Equal(30, settings.TicksPerSecond);
        Assert.Equal(realMinutesPerDay * secondsPerMinute * settings.TicksPerSecond, settings.TicksPerDay);
    }

    [Fact]
    public void New_world_starts_at_tick_zero_on_day_zero()
    {
        var clock = new SimulationClock(SmallDay);

        Assert.Equal(new SimulationTick(0, 0, 0), clock.Current);
    }

    [Fact]
    public void Advancing_past_the_last_tick_of_a_day_starts_the_next_day()
    {
        var clock = new SimulationClock(SmallDay);

        for (int i = 0; i < TicksPerDay; i++)
        {
            clock.Advance();
        }

        Assert.Equal(new SimulationTick(TicksPerDay, 1, 0), clock.Current);
    }

    [Fact]
    public void Loaded_world_resumes_at_the_saved_tick()
    {
        const ulong savedTick = 1_234_567_890_123UL;

        var clock = new SimulationClock(SmallDay, savedTick);

        Assert.Equal(savedTick, clock.Current.Index);
        Assert.Equal(savedTick / TicksPerDay, clock.Current.Day);
        Assert.Equal((int)(savedTick % TicksPerDay), clock.Current.TickOfDay);
    }

    [Theory]
    [InlineData(0, TicksPerDay, MaxCatchUpTicks)]
    [InlineData(TicksPerSecond, 0, MaxCatchUpTicks)]
    [InlineData(TicksPerSecond, TicksPerDay, 0)]
    public void Settings_reject_non_positive_values(int ticksPerSecond, int ticksPerDay, int maxCatchUpTicks)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SimulationSettings(ticksPerSecond, ticksPerDay, maxCatchUpTicks));
    }
}
