using System.Numerics;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Cognition.Decision;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Events;
using SumonerWorld.Core.Randomness;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.Spatial;

namespace SumonerWorld.Scenarios.B5;

/// <summary>Perceives neighbours within 40 m, one third of the creatures per tick (AI LOD time slicing, AI Bible §9.2).</summary>
internal sealed class PerceptionSystem(IEcsWorld world, ISpatialIndex grid, IEventBus events) : ISimulationSystem
{
    private const float Radius = 40f;
    private const int Slices = 3;

    private readonly Entity[] _buffer = new Entity[64];
    private readonly ComponentStore<Position> _positions = world.Store<Position>();

    public SimulationStage Stage => SimulationStage.Perception;

    public void Execute(in SimulationTick tick)
    {
        int slice = (int)(tick.Index % Slices);
        ReadOnlySpan<Entity> entities = _positions.Entities;
        Span<Position> positions = _positions.Components;
        for (int i = 0; i < entities.Length; i++)
        {
            if (entities[i].Index % Slices != slice)
            {
                continue;
            }

            SpatialQueryResult result = grid.QueryRadius(positions[i].Value, Radius, _buffer);
            events.Publish(new NeighbourCount(entities[i], result.Count - 1));
        }
    }
}

/// <summary>
/// Utility AI (M1-08): each creature decides at 2 Hz (time-sliced, AI Bible §9.2). The chosen goal sets its speed in
/// <see cref="WanderSystem"/>; Eat and Rest satisfy their need while they last.
/// </summary>
internal sealed class DecisionSystem(IEcsWorld world, IUtilityEvaluator evaluator, ICreatureMindStore minds, GoalSet goals, IRandom random, B5Goals ids)
    : ISimulationSystem
{
    private const int Slices = 15;
    private const int FullHealth = 1000;
    private const int SatisfyPerDecision = 2;

    private readonly ComponentStore<CurrentGoal> _goals = world.Store<CurrentGoal>();
    private readonly ComponentStore<MindRef> _minds = world.Store<MindRef>();

    public SimulationStage Stage => SimulationStage.AiDecision;

    public void Execute(in SimulationTick tick)
    {
        int slice = (int)(tick.Index % Slices);
        ReadOnlySpan<Entity> entities = _goals.Entities;
        Span<CurrentGoal> current = _goals.Components;
        for (int i = 0; i < entities.Length; i++)
        {
            if (entities[i].Index % Slices != slice)
            {
                continue;
            }

            MindHandle mind = _minds.Get(entities[i]).Handle;
            var context = new DecisionContext(goals, tick.Index, current[i].Goal, Focus: null, FocusDistance: 0, FullHealth);
            current[i] = new CurrentGoal(evaluator.Decide(mind, context, random).Goal);

            if (current[i].Goal == ids.Eat)
            {
                minds.SatisfyNeed(mind, Need.Hunger, SatisfyPerDecision, tick.Index);
            }
            else if (current[i].Goal == ids.Rest)
            {
                minds.SatisfyNeed(mind, Need.Rest, SatisfyPerDecision, tick.Index);
            }
        }
    }
}

/// <summary>Goal ids the B5 stand-in behaviours react to, resolved from the shipped goal data.</summary>
internal sealed record B5Goals(ushort Flee, ushort Eat, ushort Rest, ushort Socialize);

/// <summary>Picks a new random heading when a creature's timer runs out; speed depends on the current goal.</summary>
internal sealed class WanderSystem(IEcsWorld world, IRandom random, float speed, B5Goals ids) : ISimulationSystem
{
    private const int MinWanderTicks = 30;
    private const int MaxWanderTicks = 150;
    private const int Degrees = 360;

    private readonly ComponentStore<WanderTimer> _timers = world.Store<WanderTimer>();
    private readonly ComponentStore<Velocity> _velocities = world.Store<Velocity>();
    private readonly ComponentStore<CurrentGoal> _goals = world.Store<CurrentGoal>();

    public SimulationStage Stage => SimulationStage.Behaviour;

    public void Execute(in SimulationTick tick)
    {
        ReadOnlySpan<Entity> entities = _timers.Entities;
        Span<WanderTimer> timers = _timers.Components;
        for (int i = 0; i < entities.Length; i++)
        {
            if (--timers[i].TicksLeft > 0)
            {
                continue;
            }

            ushort goal = _goals.Get(entities[i]).Goal;
            float goalSpeed = goal == ids.Flee ? speed * 3 : goal == ids.Eat || goal == ids.Rest ? 0f : goal == ids.Socialize ? speed / 2 : speed;
            float angle = random.NextInt(0, Degrees) * (MathF.PI / 180f);
            _velocities.Get(entities[i]) = new Velocity(new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle)) * goalSpeed);
            timers[i] = new WanderTimer(random.NextInt(MinWanderTicks, MaxWanderTicks));
        }
    }
}

/// <summary>
/// Crowded creatures (more than 6 neighbours) remember the spot as dangerous (memory event <c>PlaceDanger</c>, subject = the
/// 16 m grid cell), appraise it (opinion → emotions → emote, M1-07) and pick a new heading on the next tick.
/// </summary>
internal sealed class CrowdReactionSystem(IEcsWorld world, IEventBus events, ICreatureMindStore minds, IAppraisal appraisal, ushort placeDanger) : ISimulationSystem
{
    private const int CrowdedNeighbours = 6;
    private const float CellSize = SpatialGridSettings.DefaultCellSize;
    private const ulong CellsPerRow = 1024;

    private readonly ComponentStore<WanderTimer> _timers = world.Store<WanderTimer>();
    private readonly ComponentStore<Position> _positions = world.Store<Position>();
    private readonly ComponentStore<MindRef> _minds = world.Store<MindRef>();
    private readonly ComponentStore<CurrentEmote> _emotes = world.Store<CurrentEmote>();

    public SimulationStage Stage => SimulationStage.Behaviour;

    public void Execute(in SimulationTick tick)
    {
        foreach (NeighbourCount sensed in events.Read<NeighbourCount>())
        {
            if (sensed.Count > CrowdedNeighbours)
            {
                Vector3 position = _positions.Get(sensed.Creature).Value;
                ulong cell = ((ulong)(position.Z / CellSize) * CellsPerRow) + (ulong)(position.X / CellSize);
                MindHandle mind = _minds.Get(sensed.Creature).Handle;
                var place = new MemorySubject(SubjectKind.Place, cell);
                minds.Remember(mind, placeDanger, place, tick.Index);
                _emotes.Get(sensed.Creature) = new CurrentEmote(appraisal.Appraise(mind, place, tick.Index));
                _timers.Get(sensed.Creature) = new WanderTimer(1);
            }
        }
    }
}

/// <summary>Moves creatures, bounces them off the region edges and updates the spatial index.</summary>
internal sealed class MovementSystem(IEcsWorld world, ISpatialIndex grid, float regionSize) : ISimulationSystem
{
    private readonly ComponentStore<Velocity> _velocities = world.Store<Velocity>();
    private readonly ComponentStore<Position> _positions = world.Store<Position>();

    public SimulationStage Stage => SimulationStage.Movement;

    public void Execute(in SimulationTick tick)
    {
        ReadOnlySpan<Entity> entities = _velocities.Entities;
        Span<Velocity> velocities = _velocities.Components;
        for (int i = 0; i < entities.Length; i++)
        {
            ref Position position = ref _positions.Get(entities[i]);
            Vector3 next = position.Value + velocities[i].Value;
            if (next.X < 0 || next.X > regionSize || next.Z < 0 || next.Z > regionSize)
            {
                velocities[i] = new Velocity(-velocities[i].Value);
                next = Vector3.Clamp(next, Vector3.Zero, new Vector3(regionSize, 0, regionSize));
            }

            position = new Position(next);
            grid.Move(entities[i], next);
        }
    }
}

/// <summary>Every 30 ticks one creature dies and a new one spawns (ECS, spatial index and stable id churn).</summary>
internal sealed class ChurnSystem(B5Scenario scenario, IEcsWorld world) : ISimulationSystem
{
    private const int Interval = 30;

    private readonly ComponentStore<Position> _positions = world.Store<Position>();

    public SimulationStage Stage => SimulationStage.Ecosystem;

    public void Execute(in SimulationTick tick)
    {
        if (tick.Index % Interval != 0 || _positions.Count == 0)
        {
            return;
        }

        scenario.Despawn(_positions.Entities[0]);
        scenario.Spawn();
    }
}
