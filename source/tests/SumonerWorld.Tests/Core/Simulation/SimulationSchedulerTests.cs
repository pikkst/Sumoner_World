using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Core.Events;
using SumonerWorld.Core.Simulation;

namespace SumonerWorld.Tests.Core.Simulation;

public class SimulationSchedulerTests
{
    private const int EventCapacity = 8;

    [Fact]
    public void Systems_run_in_stage_order_whatever_the_registration_order()
    {
        var (scheduler, _, _) = CreateScheduler();
        var log = new List<string>();
        scheduler.Register(new RecordingSystem("persist", SimulationStage.Persistence, log));
        scheduler.Register(new RecordingSystem("move", SimulationStage.Movement, log));
        scheduler.Register(new RecordingSystem("inputs", SimulationStage.ApplyInputs, log));

        scheduler.RunTick();

        Assert.Equal(["inputs@0", "move@0", "persist@0"], log);
    }

    [Fact]
    public void Systems_in_the_same_stage_run_in_registration_order()
    {
        var (scheduler, _, _) = CreateScheduler();
        var log = new List<string>();
        scheduler.Register(new RecordingSystem("b", SimulationStage.Combat, log));
        scheduler.Register(new RecordingSystem("a", SimulationStage.Combat, log));

        scheduler.RunTick();

        Assert.Equal(["b@0", "a@0"], log);
    }

    [Fact]
    public void Each_tick_sees_its_own_time_and_then_the_clock_advances()
    {
        var (scheduler, clock, _) = CreateScheduler();
        var log = new List<string>();
        scheduler.Register(new RecordingSystem("s", SimulationStage.Movement, log));

        scheduler.RunTick();
        scheduler.RunTick();

        Assert.Equal(["s@0", "s@1"], log);
        Assert.Equal(2UL, clock.Current.Index);
    }

    [Fact]
    public void Events_reach_later_stages_in_the_same_tick_and_are_gone_next_tick()
    {
        var (scheduler, _, events) = CreateScheduler();
        events.Register<SeenEvent>(EventCapacity);
        var reader = new ReadingSystem(events);
        scheduler.Register(reader);
        scheduler.Register(new PublishOnFirstTickSystem(events));

        scheduler.RunTick();
        scheduler.RunTick();

        Assert.Equal([1, 0], reader.CountsPerTick);
    }

    [Fact]
    public void Registering_during_a_tick_is_rejected()
    {
        var (scheduler, _, _) = CreateScheduler();
        scheduler.Register(new RegisteringSystem(scheduler));

        Assert.Throws<InvalidOperationException>(scheduler.RunTick);
    }

    [Fact]
    public void A_failing_system_is_not_swallowed()
    {
        var (scheduler, _, _) = CreateScheduler();
        scheduler.Register(new ThrowingSystem());

        Assert.Throws<InvalidOperationException>(scheduler.RunTick);
    }

    [Fact]
    public void Steady_state_ticks_with_events_allocate_nothing()
    {
        // TDD §1.2 / R21: 0 bytes per tick on the sim thread after warm-up.
        const int warmUpTicks = 10;
        const int measuredTicks = 1_000;
        var (scheduler, _, events) = CreateScheduler();
        events.Register<SeenEvent>(EventCapacity);
        scheduler.Register(new PublishEveryTickSystem(events));
        scheduler.Register(new SummingSystem(events));

        for (int i = 0; i < warmUpTicks; i++)
        {
            scheduler.RunTick();
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < measuredTicks; i++)
        {
            scheduler.RunTick();
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Metrics_see_every_tick_and_stage_boundary_in_order()
    {
        var clock = new SimulationClock(SimulationSettings.Default);
        var metrics = new RecordingMetrics();
        var scheduler = new SimulationScheduler(clock, new EventBus(), metrics);
        int stageCount = Enum.GetValues<SimulationStage>().Length;

        scheduler.RunTick();

        Assert.Equal("tick+0", metrics.Log[0]);
        Assert.Equal("stage+ApplyInputs", metrics.Log[1]);
        Assert.Equal("stage-Persistence", metrics.Log[^2]);
        Assert.Equal("tick-0", metrics.Log[^1]);
        Assert.Equal(2 + (2 * stageCount), metrics.Log.Count);
    }

    private static (SimulationScheduler Scheduler, SimulationClock Clock, EventBus Events) CreateScheduler()
    {
        var clock = new SimulationClock(SimulationSettings.Default);
        var events = new EventBus();
        return (new SimulationScheduler(clock, events), clock, events);
    }

    private readonly record struct SeenEvent(ulong Tick);

    private sealed class RecordingSystem(string name, SimulationStage stage, List<string> log) : ISimulationSystem
    {
        public SimulationStage Stage => stage;

        public void Execute(in SimulationTick tick) => log.Add($"{name}@{tick.Index}");
    }

    private sealed class PublishOnFirstTickSystem(EventBus events) : ISimulationSystem
    {
        public SimulationStage Stage => SimulationStage.Perception;

        public void Execute(in SimulationTick tick)
        {
            if (tick.Index == 0)
            {
                events.Publish(new SeenEvent(tick.Index));
            }
        }
    }

    private sealed class RecordingMetrics : IMetrics
    {
        public List<string> Log { get; } = [];

        public void TickStarted(in SimulationTick tick) => Log.Add($"tick+{tick.Index}");

        public void StageStarted(SimulationStage stage) => Log.Add($"stage+{stage}");

        public void StageCompleted(SimulationStage stage) => Log.Add($"stage-{stage}");

        public void TickCompleted(in SimulationTick tick) => Log.Add($"tick-{tick.Index}");
    }

    private sealed class PublishEveryTickSystem(EventBus events) : ISimulationSystem
    {
        public SimulationStage Stage => SimulationStage.Perception;

        public void Execute(in SimulationTick tick) => events.Publish(new SeenEvent(tick.Index));
    }

    private sealed class SummingSystem(EventBus events) : ISimulationSystem
    {
        public ulong Sum { get; private set; }

        public SimulationStage Stage => SimulationStage.AiDecision;

        public void Execute(in SimulationTick tick)
        {
            foreach (SeenEvent seen in events.Read<SeenEvent>())
            {
                Sum += seen.Tick;
            }
        }
    }

    private sealed class ReadingSystem(EventBus events) : ISimulationSystem
    {
        public List<int> CountsPerTick { get; } = [];

        public SimulationStage Stage => SimulationStage.AiDecision;

        public void Execute(in SimulationTick tick) => CountsPerTick.Add(events.Read<SeenEvent>().Length);
    }

    private sealed class RegisteringSystem(SimulationScheduler scheduler) : ISimulationSystem
    {
        public SimulationStage Stage => SimulationStage.ApplyInputs;

        public void Execute(in SimulationTick tick) => scheduler.Register(this);
    }

    private sealed class ThrowingSystem : ISimulationSystem
    {
        public SimulationStage Stage => SimulationStage.Combat;

        public void Execute(in SimulationTick tick) => throw new InvalidOperationException("boom");
    }
}
