using System.Numerics;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Cognition.Behaviour;
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

/// <summary>Utility AI (M1-08): each creature decides at 2 Hz (time-sliced, AI Bible §9.2).</summary>
internal sealed class DecisionSystem(IEcsWorld world, IUtilityEvaluator evaluator, GoalSet goals, IRandom random) : ISimulationSystem
{
    private const int Slices = 15;
    private const int FullHealth = 1000;

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
            if (entities[i].Index % Slices == slice)
            {
                var context = new DecisionContext(goals, tick.Index, current[i].Goal, Focus: null, FocusDistance: 0, FullHealth);
                current[i] = new CurrentGoal(evaluator.Decide(_minds.Get(entities[i]).Handle, context, random).Goal);
            }
        }
    }
}

/// <summary>Behaviour trees (M1-09): every creature's tree for its current goal runs each tick and emits intents.</summary>
internal sealed class BehaviourSystem(IEcsWorld world, IBehaviourTreeRunner runner) : ISimulationSystem
{
    private const int FullHealth = 1000;

    private readonly ComponentStore<CurrentGoal> _goals = world.Store<CurrentGoal>();
    private readonly ComponentStore<MindRef> _minds = world.Store<MindRef>();

    public SimulationStage Stage => SimulationStage.Behaviour;

    public void Execute(in SimulationTick tick)
    {
        ReadOnlySpan<Entity> entities = _goals.Entities;
        Span<CurrentGoal> goals = _goals.Components;
        for (int i = 0; i < entities.Length; i++)
        {
            var context = new BehaviourContext(entities[i], goals[i].Goal, tick.Index, Focus: null, FocusDistance: 0, FullHealth);
            runner.Tick(_minds.Get(entities[i]).Handle, context);
        }
    }
}

/// <summary>
/// Crowded creatures (more than 6 neighbours) remember the spot as dangerous (memory event <c>PlaceDanger</c>, subject = the
/// 16 m grid cell) and appraise it (opinion → emotions → emote, M1-07).
/// </summary>
internal sealed class CrowdReactionSystem(IEcsWorld world, IEventBus events, ICreatureMindStore minds, IAppraisal appraisal, ushort placeDanger) : ISimulationSystem
{
    private const int CrowdedNeighbours = 6;
    private const float CellSize = SpatialGridSettings.DefaultCellSize;
    private const ulong CellsPerRow = 1024;

    private readonly ComponentStore<Position> _positions = world.Store<Position>();
    private readonly ComponentStore<MindRef> _minds = world.Store<MindRef>();
    private readonly ComponentStore<CurrentEmote> _emotes = world.Store<CurrentEmote>();

    public SimulationStage Stage => SimulationStage.Behaviour;

    public void Execute(in SimulationTick tick)
    {
        foreach (NeighbourCount sensed in events.Read<NeighbourCount>())
        {
            if (sensed.Count <= CrowdedNeighbours)
            {
                continue;
            }

            Vector3 position = _positions.Get(sensed.Creature).Value;
            ulong cell = ((ulong)(position.Z / CellSize) * CellsPerRow) + (ulong)(position.X / CellSize);
            MindHandle mind = _minds.Get(sensed.Creature).Handle;
            var place = new MemorySubject(SubjectKind.Place, cell);
            minds.Remember(mind, placeDanger, place, tick.Index);
            _emotes.Get(sensed.Creature) = new CurrentEmote(appraisal.Appraise(mind, place, tick.Index));
        }
    }
}

/// <summary>
/// Carries out behaviour tree intents (stand-in for the M1-15 authoritative intent pipeline): MoveTo Wander picks a point
/// 10–40 m away; Stop halts; Interact satisfies its need; Emote sets the shown emote. Focus targets fail (B5 has no players).
/// </summary>
internal sealed class IntentSystem(
    IEcsWorld world, IEventBus events, IBehaviourTreeRunner runner, ICreatureMindStore minds, IRandom random, float walkSpeed, float regionSize)
    : ISimulationSystem
{
    private const int MinWanderMetres = 10;
    private const int MaxWanderMetres = 40;
    private const int Degrees = 360;
    private const int SatisfyPerInteraction = 10;
    private const float Percent = 100f;

    private readonly ComponentStore<Position> _positions = world.Store<Position>();
    private readonly ComponentStore<MoveTarget> _targets = world.Store<MoveTarget>();
    private readonly ComponentStore<MindRef> _minds = world.Store<MindRef>();
    private readonly ComponentStore<CurrentEmote> _emotes = world.Store<CurrentEmote>();

    public SimulationStage Stage => SimulationStage.Movement;

    public int Interactions { get; private set; }

    public void Execute(in SimulationTick tick)
    {
        foreach (CreatureIntent intent in events.Read<CreatureIntent>())
        {
            MindHandle mind = _minds.Get(intent.Actor).Handle;
            switch (intent.Kind)
            {
                case IntentKind.MoveTo when intent.Target == MoveTargetMode.Wander:
                    _targets.Get(intent.Actor) = new MoveTarget(WanderPoint(intent.Actor), walkSpeed * intent.Speed / Percent, 1);
                    break;
                case IntentKind.MoveTo:
                    runner.ReportOutcome(mind, succeeded: false);
                    break;
                case IntentKind.Stop:
                    _targets.Get(intent.Actor) = _targets.Get(intent.Actor) with { Active = 0 };
                    break;
                case IntentKind.Interact:
                    minds.SatisfyNeed(mind, NeedFor(intent.Verb), SatisfyPerInteraction, tick.Index);
                    Interactions++;
                    break;
                case IntentKind.Emote:
                    _emotes.Get(intent.Actor) = new CurrentEmote(intent.Emote);
                    break;
                default:
                    break;
            }
        }
    }

    private static Need NeedFor(InteractVerb verb) => verb switch
    {
        InteractVerb.Eat or InteractVerb.Hunt => Need.Hunger,
        InteractVerb.Rest => Need.Rest,
        _ => Need.Social,
    };

    private Vector3 WanderPoint(Entity actor)
    {
        float angle = random.NextInt(0, Degrees) * (MathF.PI / 180f);
        float distance = random.NextInt(MinWanderMetres, MaxWanderMetres);
        Vector3 offset = new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle)) * distance;
        return Vector3.Clamp(_positions.Get(actor).Value + offset, Vector3.Zero, new Vector3(regionSize, 0, regionSize));
    }
}

/// <summary>Moves creatures toward their targets, updates the spatial index and reports arrival to the behaviour tree.</summary>
internal sealed class MovementSystem(IEcsWorld world, ISpatialIndex grid, IBehaviourTreeRunner runner) : ISimulationSystem
{
    private readonly ComponentStore<MoveTarget> _targets = world.Store<MoveTarget>();
    private readonly ComponentStore<Position> _positions = world.Store<Position>();
    private readonly ComponentStore<MindRef> _minds = world.Store<MindRef>();

    public SimulationStage Stage => SimulationStage.Movement;

    public void Execute(in SimulationTick tick)
    {
        ReadOnlySpan<Entity> entities = _targets.Entities;
        Span<MoveTarget> targets = _targets.Components;
        for (int i = 0; i < entities.Length; i++)
        {
            if (targets[i].Active == 0)
            {
                continue;
            }

            ref Position position = ref _positions.Get(entities[i]);
            Vector3 toTarget = targets[i].Target - position.Value;
            float distance = toTarget.Length();
            if (distance <= targets[i].Speed)
            {
                position = new Position(targets[i].Target);
                targets[i] = targets[i] with { Active = 0 };
                runner.ReportOutcome(_minds.Get(entities[i]).Handle, succeeded: true);
            }
            else
            {
                position = new Position(position.Value + (toTarget / distance * targets[i].Speed));
            }

            grid.Move(entities[i], position.Value);
        }
    }
}

/// <summary>Every 30 ticks one creature dies and a new one spawns (ECS, spatial index, mind store and stable id churn).</summary>
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
