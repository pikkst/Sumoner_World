using System.Numerics;
using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Cognition.Behaviour;
using SumonerWorld.Core.Cognition.Decision;
using SumonerWorld.Core.Cognition.Lod;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Cognition.Perception;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Events;
using SumonerWorld.Core.Randomness;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.Spatial;

namespace SumonerWorld.Scenarios.B5;

/// <summary>Maps entities to memory subjects: stand-in players by account id, creatures by stable id.</summary>
internal sealed class B5SubjectResolver(IEcsWorld world) : ISubjectResolver
{
    private readonly ComponentStore<PlayerTag> _players = world.Store<PlayerTag>();
    private readonly ComponentStore<MindRef> _minds = world.Store<MindRef>();
    private readonly ComponentStore<StableId> _ids = world.Store<StableId>();

    public bool TryResolve(Entity entity, out MemorySubject subject)
    {
        if (_players.Has(entity))
        {
            subject = new MemorySubject(SubjectKind.Player, _players.Get(entity).AccountId);
            return true;
        }

        if (_minds.Has(entity))
        {
            subject = new MemorySubject(SubjectKind.Creature, _ids.Get(entity).Value);
            return true;
        }

        subject = default;
        return false;
    }

    public MemorySubject? FocusSubject(in FocusRef focus) =>
        focus.Has != 0 && TryResolve(focus.Entity, out MemorySubject subject) ? subject : null;
}

/// <summary>Stand-in players walk a fixed square route around the region (deterministic).</summary>
internal sealed class PlayerRouteSystem(IEcsWorld world, float regionSize, float speed) : ISimulationSystem
{
    private const float Margin = 100f;

    private readonly ComponentStore<PlayerTag> _players = world.Store<PlayerTag>();
    private readonly ComponentStore<MoveTarget> _targets = world.Store<MoveTarget>();

    public SimulationStage Stage => SimulationStage.ApplyInputs;

    public static Vector3 Waypoint(int index, float regionSize) => (index & 3) switch
    {
        0 => new Vector3(Margin, 0, Margin),
        1 => new Vector3(regionSize - Margin, 0, Margin),
        2 => new Vector3(regionSize - Margin, 0, regionSize - Margin),
        _ => new Vector3(Margin, 0, regionSize - Margin),
    };

    public void Execute(in SimulationTick tick)
    {
        ReadOnlySpan<Entity> players = _players.Entities;
        Span<PlayerTag> tags = _players.Components;
        for (int i = 0; i < players.Length; i++)
        {
            ref MoveTarget target = ref _targets.Get(players[i]);
            if (target.Active == 0)
            {
                tags[i] = tags[i] with { Waypoint = tags[i].Waypoint + 1 };
                target = new MoveTarget(Waypoint(tags[i].Waypoint, regionSize), speed, 1);
            }
        }
    }
}

/// <summary>AI LOD (M1-10): every half second each creature's tier is set from its distance to the nearest player.</summary>
internal sealed class LodSystem(IEcsWorld world, IAiLodPolicy policy) : ISimulationSystem
{
    private const int Interval = 15;

    private readonly ComponentStore<AiTier> _tiers = world.Store<AiTier>();
    private readonly ComponentStore<Position> _positions = world.Store<Position>();
    private readonly ComponentStore<PlayerTag> _players = world.Store<PlayerTag>();

    public SimulationStage Stage => SimulationStage.Perception;

    public void Execute(in SimulationTick tick)
    {
        if (tick.Index % Interval != 0)
        {
            return;
        }

        ReadOnlySpan<Entity> creatures = _tiers.Entities;
        Span<AiTier> tiers = _tiers.Components;
        ReadOnlySpan<Entity> players = _players.Entities;
        for (int i = 0; i < creatures.Length; i++)
        {
            Vector3 position = _positions.Get(creatures[i]).Value;
            float nearest = float.PositiveInfinity;
            for (int p = 0; p < players.Length; p++)
            {
                nearest = MathF.Min(nearest, Vector3.Distance(position, _positions.Get(players[p]).Value));
            }

            tiers[i] = new AiTier(policy.Classify(tiers[i].Tier, nearest, engaged: false, loaded: true));
        }
    }
}

/// <summary>
/// Perception (M1-10) at each creature's tier rate: senses neighbours and players, picks a focus (players first), appraises it
/// (emotions react to who is there) and reports how many creatures it senses (for crowd reactions).
/// </summary>
internal sealed class PerceptionSystem(
    IEcsWorld world, IPerception perception, IAiLodPolicy policy, B5SubjectResolver resolver, IAppraisal appraisal, IEventBus events, PerceptionDefinition senses)
    : ISimulationSystem
{
    private readonly Percept[] _percepts = new Percept[16];
    private readonly ComponentStore<AiTier> _tiers = world.Store<AiTier>();
    private readonly ComponentStore<Position> _positions = world.Store<Position>();
    private readonly ComponentStore<Facing> _facings = world.Store<Facing>();
    private readonly ComponentStore<FocusRef> _focus = world.Store<FocusRef>();
    private readonly ComponentStore<MindRef> _minds = world.Store<MindRef>();
    private readonly ComponentStore<PlayerTag> _players = world.Store<PlayerTag>();

    public SimulationStage Stage => SimulationStage.Perception;

    public int PlayerFocusEvents { get; private set; }

    public void Execute(in SimulationTick tick)
    {
        ReadOnlySpan<Entity> creatures = _tiers.Entities;
        Span<AiTier> tiers = _tiers.Components;
        for (int i = 0; i < creatures.Length; i++)
        {
            Entity creature = creatures[i];
            if (!policy.IsDue(tiers[i].Tier, AiActivity.Perception, creature.Index, tick.Index))
            {
                continue;
            }

            int count = perception.Perceive(creature, _positions.Get(creature).Value, _facings.Get(creature).Value, senses, _percepts);
            MindHandle mind = _minds.Get(creature).Handle;
            ReadOnlySpan<Percept> percepts = _percepts.AsSpan(0, count);
            if (perception.SelectFocus(mind, percepts, senses, resolver, tick.Index, out FocusChoice focus))
            {
                _focus.Get(creature) = new FocusRef(focus.Entity, focus.Distance, 1);
                appraisal.Appraise(mind, focus.Subject, tick.Index);
                PlayerFocusEvents += _players.Has(focus.Entity) ? 1 : 0;
            }
            else
            {
                _focus.Get(creature) = default;
            }

            int creaturesSensed = 0;
            foreach (Percept percept in percepts)
            {
                creaturesSensed += _minds.Has(percept.Entity) ? 1 : 0;
            }

            events.Publish(new NeighbourCount(creature, creaturesSensed));
        }
    }
}

/// <summary>Utility AI (M1-08) at each creature's tier rate, with its current focus.</summary>
internal sealed class DecisionSystem(IEcsWorld world, IUtilityEvaluator evaluator, IAiLodPolicy policy, B5SubjectResolver resolver, GoalSet goals, IRandom random)
    : ISimulationSystem
{
    private const int FullHealth = 1000;

    private readonly ComponentStore<CurrentGoal> _goals = world.Store<CurrentGoal>();
    private readonly ComponentStore<AiTier> _tiers = world.Store<AiTier>();
    private readonly ComponentStore<FocusRef> _focus = world.Store<FocusRef>();
    private readonly ComponentStore<MindRef> _minds = world.Store<MindRef>();

    public SimulationStage Stage => SimulationStage.AiDecision;

    public void Execute(in SimulationTick tick)
    {
        ReadOnlySpan<Entity> entities = _goals.Entities;
        Span<CurrentGoal> current = _goals.Components;
        for (int i = 0; i < entities.Length; i++)
        {
            if (!policy.IsDue(_tiers.Get(entities[i]).Tier, AiActivity.Decision, entities[i].Index, tick.Index))
            {
                continue;
            }

            FocusRef focus = _focus.Get(entities[i]);
            var context = new DecisionContext(goals, tick.Index, current[i].Goal, resolver.FocusSubject(focus), focus.Distance, FullHealth);
            current[i] = new CurrentGoal(evaluator.Decide(_minds.Get(entities[i]).Handle, context, random).Goal);
        }
    }
}

/// <summary>Behaviour trees (M1-09) at each creature's tier rate, with its current focus.</summary>
internal sealed class BehaviourSystem(IEcsWorld world, IBehaviourTreeRunner runner, IAiLodPolicy policy, B5SubjectResolver resolver) : ISimulationSystem
{
    private const int FullHealth = 1000;

    private readonly ComponentStore<CurrentGoal> _goals = world.Store<CurrentGoal>();
    private readonly ComponentStore<AiTier> _tiers = world.Store<AiTier>();
    private readonly ComponentStore<FocusRef> _focus = world.Store<FocusRef>();
    private readonly ComponentStore<MindRef> _minds = world.Store<MindRef>();

    public SimulationStage Stage => SimulationStage.Behaviour;

    public void Execute(in SimulationTick tick)
    {
        ReadOnlySpan<Entity> entities = _goals.Entities;
        Span<CurrentGoal> goals = _goals.Components;
        for (int i = 0; i < entities.Length; i++)
        {
            if (!policy.IsDue(_tiers.Get(entities[i]).Tier, AiActivity.Behaviour, entities[i].Index, tick.Index))
            {
                continue;
            }

            FocusRef focus = _focus.Get(entities[i]);
            var context = new BehaviourContext(entities[i], goals[i].Goal, tick.Index, resolver.FocusSubject(focus), focus.Distance, FullHealth);
            runner.Tick(_minds.Get(entities[i]).Handle, context);
        }
    }
}

/// <summary>
/// Crowded creatures (more than 6 creatures sensed) remember the spot as dangerous (memory event <c>PlaceDanger</c>, subject =
/// the 16 m grid cell) and appraise it (opinion → emotions → emote, M1-07).
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
/// Carries out behaviour tree intents (stand-in for the M1-15 authoritative intent pipeline): MoveTo goes to a wander point, to the
/// focus or away from it; Stop halts; Interact satisfies its need; Emote sets the shown emote.
/// </summary>
internal sealed class IntentSystem(
    IEcsWorld world, IEventBus events, IBehaviourTreeRunner runner, ICreatureMindStore minds, IRandom random, float walkSpeed, float regionSize)
    : ISimulationSystem
{
    private const int MinWanderMetres = 10;
    private const int MaxWanderMetres = 40;
    private const float FleeMetres = 30f;
    private const int Degrees = 360;
    private const int SatisfyPerInteraction = 10;
    private const float Percent = 100f;

    private readonly ComponentStore<Position> _positions = world.Store<Position>();
    private readonly ComponentStore<MoveTarget> _targets = world.Store<MoveTarget>();
    private readonly ComponentStore<MindRef> _minds = world.Store<MindRef>();
    private readonly ComponentStore<CurrentEmote> _emotes = world.Store<CurrentEmote>();
    private readonly ComponentStore<FocusRef> _focus = world.Store<FocusRef>();

    public SimulationStage Stage => SimulationStage.Movement;

    public int Interactions { get; private set; }

    public void Execute(in SimulationTick tick)
    {
        foreach (CreatureIntent intent in events.Read<CreatureIntent>())
        {
            MindHandle mind = _minds.Get(intent.Actor).Handle;
            switch (intent.Kind)
            {
                case IntentKind.MoveTo:
                    if (TryResolveTarget(intent.Actor, intent.Target, out Vector3 target))
                    {
                        _targets.Get(intent.Actor) = new MoveTarget(target, walkSpeed * intent.Speed / Percent, 1);
                    }
                    else
                    {
                        runner.ReportOutcome(mind, succeeded: false);
                    }

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

    private bool TryResolveTarget(Entity actor, MoveTargetMode mode, out Vector3 target)
    {
        Vector3 position = _positions.Get(actor).Value;
        FocusRef focus = _focus.Get(actor);
        Vector3 bounds = new(regionSize, 0, regionSize);
        switch (mode)
        {
            case MoveTargetMode.Wander:
                float angle = random.NextInt(0, Degrees) * (MathF.PI / 180f);
                float distance = random.NextInt(MinWanderMetres, MaxWanderMetres);
                target = Vector3.Clamp(position + (new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle)) * distance), Vector3.Zero, bounds);
                return true;
            case MoveTargetMode.Focus when focus.Has != 0 && _positions.Has(focus.Entity):
                target = _positions.Get(focus.Entity).Value;
                return true;
            case MoveTargetMode.AwayFromFocus when focus.Has != 0 && _positions.Has(focus.Entity):
                Vector3 away = position - _positions.Get(focus.Entity).Value;
                away = away.LengthSquared() > 0 ? Vector3.Normalize(away) : Vector3.UnitX;
                target = Vector3.Clamp(position + (away * FleeMetres), Vector3.Zero, bounds);
                return true;
            default:
                target = default;
                return false;
        }
    }
}

/// <summary>Moves entities toward their targets, turns them to face the way they go, updates the spatial index and reports arrival.</summary>
internal sealed class MovementSystem(IEcsWorld world, ISpatialIndex grid, IBehaviourTreeRunner runner) : ISimulationSystem
{
    private readonly ComponentStore<MoveTarget> _targets = world.Store<MoveTarget>();
    private readonly ComponentStore<Position> _positions = world.Store<Position>();
    private readonly ComponentStore<Facing> _facings = world.Store<Facing>();
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
                if (_minds.Has(entities[i]))
                {
                    runner.ReportOutcome(_minds.Get(entities[i]).Handle, succeeded: true);
                }
            }
            else
            {
                Vector3 direction = toTarget / distance;
                position = new Position(position.Value + (direction * targets[i].Speed));
                _facings.Get(entities[i]) = new Facing(direction);
            }

            grid.Move(entities[i], position.Value);
        }
    }
}

/// <summary>
/// Every 10 s each stand-in player offers a Pact (feed daily + protect young) to the wild creatures that are watching it from close
/// by (M1-11). Most strangers refuse; the refusals are remembered.
/// </summary>
internal sealed class PactOfferSystem(IEcsWorld world, IBondingRules bonding, B5SubjectResolver resolver, SpeciesDefinition species, PactTerms terms)
    : ISimulationSystem
{
    private const int Interval = 300;
    private const int CloseEnough = 400;

    private readonly ComponentStore<FocusRef> _focus = world.Store<FocusRef>();
    private readonly ComponentStore<MindRef> _minds = world.Store<MindRef>();
    private readonly ComponentStore<PlayerTag> _players = world.Store<PlayerTag>();

    public SimulationStage Stage => SimulationStage.Transactions;

    public int Offers { get; private set; }

    public int Accepted { get; private set; }

    public void Execute(in SimulationTick tick)
    {
        if (tick.Index % Interval != 0)
        {
            return;
        }

        ReadOnlySpan<Entity> creatures = _focus.Entities;
        Span<FocusRef> focus = _focus.Components;
        for (int i = 0; i < creatures.Length; i++)
        {
            if (focus[i].Has == 0 || focus[i].Distance > CloseEnough || !_players.Has(focus[i].Entity)
                || resolver.FocusSubject(focus[i]) is not MemorySubject player)
            {
                continue;
            }

            MindHandle mind = _minds.Get(creatures[i]).Handle;
            if (bonding.GetBond(mind).State != OwnershipState.Wild)
            {
                continue;
            }

            Offers++;
            Accepted += bonding.OfferPact(mind, species, player, terms, reputation: 0, tick.Index, out _) == BondingOutcome.Accepted ? 1 : 0;
        }
    }
}

/// <summary>Every 30 ticks one creature dies and a new one spawns (ECS, spatial index, mind store and stable id churn).</summary>
internal sealed class ChurnSystem(B5Scenario scenario, IEcsWorld world) : ISimulationSystem
{
    private const int Interval = 30;

    private readonly ComponentStore<MindRef> _creatures = world.Store<MindRef>();

    public SimulationStage Stage => SimulationStage.Ecosystem;

    public void Execute(in SimulationTick tick)
    {
        if (tick.Index % Interval != 0 || _creatures.Count == 0)
        {
            return;
        }

        scenario.Despawn(_creatures.Entities[0]);
        scenario.Spawn();
    }
}
