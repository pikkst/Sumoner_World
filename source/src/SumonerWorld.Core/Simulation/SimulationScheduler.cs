using SumonerWorld.Core.Events;

namespace SumonerWorld.Core.Simulation;

/// <summary>
/// Runs registered systems in the fixed stage order, clears the tick's events and advances the clock (TDD §2.2).
/// Within one stage, systems run in registration order, so the whole tick is deterministic (R11).
/// </summary>
public sealed class SimulationScheduler
{
    private static readonly int StageCount = Enum.GetValues<SimulationStage>().Length;

    private readonly SimulationClock _clock;
    private readonly EventBus _events;
    private readonly List<ISimulationSystem>[] _systemsByStage;
    private bool _isRunning;

    /// <summary>Creates a scheduler that drives <paramref name="clock"/> and owns the lifetime of <paramref name="events"/>.</summary>
    public SimulationScheduler(SimulationClock clock, EventBus events)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(events);

        _clock = clock;
        _events = events;
        _systemsByStage = new List<ISimulationSystem>[StageCount];
        for (int stage = 0; stage < StageCount; stage++)
        {
            _systemsByStage[stage] = [];
        }
    }

    /// <summary>Adds a system. Allowed only between ticks.</summary>
    public void Register(ISimulationSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);

        if (_isRunning)
        {
            throw new InvalidOperationException(
                $"Cannot register {system.GetType().Name} while a tick is running; register systems before the first tick.");
        }

        int stage = (int)system.Stage;
        if (stage < 0 || stage >= StageCount)
        {
            throw new ArgumentOutOfRangeException(nameof(system), system.Stage, "Unknown simulation stage.");
        }

        _systemsByStage[stage].Add(system);
    }

    /// <summary>Simulates exactly one tick.</summary>
    public void RunTick()
    {
        _isRunning = true;
        try
        {
            SimulationTick tick = _clock.Current;
            for (int stage = 0; stage < _systemsByStage.Length; stage++)
            {
                List<ISimulationSystem> systems = _systemsByStage[stage];
                for (int i = 0; i < systems.Count; i++)
                {
                    systems[i].Execute(in tick);
                }
            }

            _events.EndTick();
            _clock.Advance();
        }
        finally
        {
            _isRunning = false;
        }
    }
}
