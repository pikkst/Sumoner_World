using System.Numerics;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Cognition.Behaviour;
using SumonerWorld.Core.Cognition.Decision;
using SumonerWorld.Core.Cognition.Lod;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Cognition.Perception;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecosystem;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Events;
using SumonerWorld.Core.Randomness;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.Spatial;

namespace SumonerWorld.Core.World;

/// <summary>Maps entities to memory subjects: player avatars by account id, creatures by stable id (AI Bible §4.1).</summary>
public sealed class CreatureSubjectResolver(IEcsWorld world) : ISubjectResolver
{
    private readonly ComponentStore<PlayerAvatar> _players = world.Store<PlayerAvatar>();
    private readonly ComponentStore<MindRef> _minds = world.Store<MindRef>();
    private readonly ComponentStore<StableId> _ids = world.Store<StableId>();

    /// <inheritdoc />
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

    /// <summary>The subject of a creature's focus, or null when it has none (or the focus is gone).</summary>
    public MemorySubject? FocusSubject(in FocusRef focus) =>
        focus.Has != 0 && TryResolve(focus.Entity, out MemorySubject subject) ? subject : null;
}

/// <summary>AI LOD (AI Bible §9.1): every half second each creature's tier is set from its distance to the nearest player.</summary>
public sealed class LodSystem(IEcsWorld world, IAiLodPolicy policy) : ISimulationSystem
{
    private const int Interval = 15;

    private readonly ComponentStore<AiTier> _tiers = world.Store<AiTier>();
    private readonly ComponentStore<Position> _positions = world.Store<Position>();
    private readonly ComponentStore<PlayerAvatar> _players = world.Store<PlayerAvatar>();

    /// <inheritdoc />
    public SimulationStage Stage => SimulationStage.Perception;

    /// <inheritdoc />
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
/// Perception (AI Bible §2) at each creature's tier rate with its species' senses: picks a focus (players first) and appraises it,
/// tells the first herd-mate it senses its rumours (AI Bible §4.5) and publishes how many creatures it senses.
/// </summary>
public sealed class PerceptionSystem(
    IEcsWorld world,
    IPerception perception,
    IAiLodPolicy policy,
    CreatureSubjectResolver resolver,
    IAppraisal appraisal,
    ICreatureMindStore minds,
    IEventBus events,
    IReadOnlyList<SpeciesDefinition> species)
    : ISimulationSystem
{
    private const int PerceptCapacity = 16;
    private const int RumoursPerMeeting = 3;

    private readonly Percept[] _percepts = new Percept[PerceptCapacity];
    private readonly ComponentStore<AiTier> _tiers = world.Store<AiTier>();
    private readonly ComponentStore<Position> _positions = world.Store<Position>();
    private readonly ComponentStore<Facing> _facings = world.Store<Facing>();
    private readonly ComponentStore<FocusRef> _focus = world.Store<FocusRef>();
    private readonly ComponentStore<MindRef> _minds = world.Store<MindRef>();
    private readonly ComponentStore<PlayerAvatar> _players = world.Store<PlayerAvatar>();
    private readonly ComponentStore<GroupRef> _groups = world.Store<GroupRef>();
    private readonly ComponentStore<SpeciesRef> _species = world.Store<SpeciesRef>();

    /// <inheritdoc />
    public SimulationStage Stage => SimulationStage.Perception;

    /// <summary>Times a creature chose a player as its focus.</summary>
    public int PlayerFocusEvents { get; private set; }

    /// <summary>Rumours passed between group members.</summary>
    public int RumoursShared { get; private set; }

    /// <inheritdoc />
    public void Execute(in SimulationTick tick)
    {
        ReadOnlySpan<Entity> creatures = _tiers.Entities;
        Span<AiTier> tiers = _tiers.Components;
        for (int i = 0; i < creatures.Length; i++)
        {
            Entity creature = creatures[i];
            if (policy.IsDue(tiers[i].Tier, AiActivity.Perception, creature.Index, tick.Index))
            {
                Perceive(creature, tick.Index);
            }
        }
    }

    private void Perceive(Entity creature, ulong now)
    {
        PerceptionDefinition senses = species[_species.Get(creature).Index].Perception;
        int count = perception.Perceive(creature, _positions.Get(creature).Value, _facings.Get(creature).Value, senses, _percepts);
        MindHandle mind = _minds.Get(creature).Handle;
        ReadOnlySpan<Percept> percepts = _percepts.AsSpan(0, count);
        if (perception.SelectFocus(mind, percepts, senses, resolver, now, out FocusChoice focus))
        {
            _focus.Get(creature) = new FocusRef(focus.Entity, focus.Distance, 1);
            appraisal.Appraise(mind, focus.Subject, now);
            PlayerFocusEvents += _players.Has(focus.Entity) ? 1 : 0;
        }
        else
        {
            _focus.Get(creature) = default;
        }

        int creaturesSensed = 0;
        bool told = false;
        foreach (Percept percept in percepts)
        {
            if (!_minds.Has(percept.Entity))
            {
                continue;
            }

            creaturesSensed++;
            if (!told && _groups.Get(percept.Entity).Group == _groups.Get(creature).Group)
            {
                RumoursShared += minds.ShareRumours(mind, _minds.Get(percept.Entity).Handle, now, RumoursPerMeeting);
                told = true;
            }
        }

        events.Publish(new NeighboursSensed(creature, creaturesSensed));
    }
}

/// <summary>Utility AI (AI Bible §7) at each creature's tier rate, with its species' goals and current focus.</summary>
public sealed class DecisionSystem(
    IEcsWorld world, IUtilityEvaluator evaluator, IAiLodPolicy policy, CreatureSubjectResolver resolver, IReadOnlyList<GoalSet> goalsBySpecies, IRandom random)
    : ISimulationSystem
{
    private const int FullHealth = 1000;

    private readonly ComponentStore<CurrentGoal> _goals = world.Store<CurrentGoal>();
    private readonly ComponentStore<AiTier> _tiers = world.Store<AiTier>();
    private readonly ComponentStore<FocusRef> _focus = world.Store<FocusRef>();
    private readonly ComponentStore<MindRef> _minds = world.Store<MindRef>();
    private readonly ComponentStore<SpeciesRef> _species = world.Store<SpeciesRef>();

    /// <inheritdoc />
    public SimulationStage Stage => SimulationStage.AiDecision;

    /// <inheritdoc />
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
            GoalSet goals = goalsBySpecies[_species.Get(entities[i]).Index];
            var context = new DecisionContext(goals, tick.Index, current[i].Goal, resolver.FocusSubject(focus), focus.Distance, FullHealth);
            current[i] = new CurrentGoal(evaluator.Decide(_minds.Get(entities[i]).Handle, context, random).Goal);
        }
    }
}

/// <summary>Behaviour trees (AI Bible §7.3) at each creature's tier rate, with its current focus.</summary>
public sealed class BehaviourSystem(IEcsWorld world, IBehaviourTreeRunner runner, IAiLodPolicy policy, CreatureSubjectResolver resolver) : ISimulationSystem
{
    private const int FullHealth = 1000;

    private readonly ComponentStore<CurrentGoal> _goals = world.Store<CurrentGoal>();
    private readonly ComponentStore<AiTier> _tiers = world.Store<AiTier>();
    private readonly ComponentStore<FocusRef> _focus = world.Store<FocusRef>();
    private readonly ComponentStore<MindRef> _minds = world.Store<MindRef>();

    /// <inheritdoc />
    public SimulationStage Stage => SimulationStage.Behaviour;

    /// <inheritdoc />
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
/// Carries out creature intents from the behaviour trees (AI Bible §7.3): MoveTo goes to a wander point 10–40 m away, to the focus or
/// away from it (staying inside the world bounds); Stop halts; Interact satisfies its need; Emote sets the shown emote. Player
/// intents arrive through the authoritative intent pipeline instead (TDD §3.2).
/// </summary>
public sealed class CreatureIntentSystem(
    IEcsWorld world, IEventBus events, IBehaviourTreeRunner runner, ICreatureMindStore minds, IRandom random, float walkSpeed, Vector3 boundsMin, Vector3 boundsMax)
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

    /// <inheritdoc />
    public SimulationStage Stage => SimulationStage.Movement;

    /// <summary>Interact intents carried out.</summary>
    public int Interactions { get; private set; }

    /// <inheritdoc />
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
        switch (mode)
        {
            case MoveTargetMode.Wander:
                float angle = random.NextInt(0, Degrees) * (MathF.PI / 180f);
                float distance = random.NextInt(MinWanderMetres, MaxWanderMetres);
                target = Vector3.Clamp(position + (new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle)) * distance), boundsMin, boundsMax);
                return true;
            case MoveTargetMode.Focus when focus.Has != 0 && _positions.Has(focus.Entity):
                target = _positions.Get(focus.Entity).Value;
                return true;
            case MoveTargetMode.AwayFromFocus when focus.Has != 0 && _positions.Has(focus.Entity):
                Vector3 away = position - _positions.Get(focus.Entity).Value;
                away = away.LengthSquared() > 0 ? Vector3.Normalize(away) : Vector3.UnitX;
                target = Vector3.Clamp(position + (away * FleeMetres), boundsMin, boundsMax);
                return true;
            default:
                target = default;
                return false;
        }
    }
}

/// <summary>Moves entities toward their targets, turns them to face the way they go, updates the spatial index and reports arrival.</summary>
public sealed class MovementSystem(IEcsWorld world, ISpatialIndex grid, IBehaviourTreeRunner runner) : ISimulationSystem
{
    private readonly ComponentStore<MoveTarget> _targets = world.Store<MoveTarget>();
    private readonly ComponentStore<Position> _positions = world.Store<Position>();
    private readonly ComponentStore<Facing> _facings = world.Store<Facing>();
    private readonly ComponentStore<MindRef> _minds = world.Store<MindRef>();

    /// <inheritdoc />
    public SimulationStage Stage => SimulationStage.Movement;

    /// <inheritdoc />
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

/// <summary>Every 10 s the period's reputation events go to the background aggregation job (AI Bible §4.5).</summary>
public sealed class ReputationFlushSystem(IReputationService reputation) : ISimulationSystem
{
    private const int Interval = 300;

    /// <inheritdoc />
    public SimulationStage Stage => SimulationStage.Ecosystem;

    /// <inheritdoc />
    public void Execute(in SimulationTick tick)
    {
        if (tick.Index % Interval == 0)
        {
            reputation.Flush();
        }
    }
}
