using System.Numerics;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Cognition.Behaviour;
using SumonerWorld.Core.Cognition.Decision;
using SumonerWorld.Core.Cognition.Lod;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Cognition.Perception;
using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecosystem;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Events;
using SumonerWorld.Core.Jobs;
using SumonerWorld.Core.Navigation;
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
/// and shows the appraisal's emote toward it (AI Bible §6.2: Grief, Grudge/Fear from a core memory, Fear, Affection, Trust,
/// Curiosity; none without a focus, and then a behaviour tree may show one). Snared creatures keep showing fear. It also tells
/// the first herd-mate it senses its rumours (AI Bible §4.5) and publishes how many creatures it senses.
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
    private readonly ComponentStore<CurrentEmote> _emotes = world.Store<CurrentEmote>();
    private readonly ComponentStore<Snared> _snared = world.Store<Snared>();

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
        Emote emote = Emote.None;
        if (perception.SelectFocus(mind, percepts, senses, resolver, now, out FocusChoice focus))
        {
            _focus.Get(creature) = new FocusRef(focus.Entity, focus.Distance, 1);
            emote = appraisal.Appraise(mind, focus.Subject, now);
            PlayerFocusEvents += _players.Has(focus.Entity) ? 1 : 0;
        }
        else
        {
            _focus.Get(creature) = default;
        }

        if (!_snared.Has(creature))
        {
            _emotes.Get(creature) = new CurrentEmote(emote);
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

/// <summary>The AI's <c>health</c> input (AI Bible §7): a creature's health as 0–1000 of full (full when it has no combat stats).</summary>
internal static class HealthInput
{
    private const int PerMille = 1000;

    public static int Of(ComponentStore<Combatant> combatants, Entity creature)
    {
        if (!combatants.Has(creature))
        {
            return PerMille;
        }

        Combatant c = combatants.Get(creature);
        return c.MaxHealth == 0 ? PerMille : (int)((long)c.Health * PerMille / c.MaxHealth);
    }
}

/// <summary>Utility AI (AI Bible §7) at each creature's tier rate, with its species' goals, current focus and health.</summary>
public sealed class DecisionSystem(
    IEcsWorld world, IUtilityEvaluator evaluator, IAiLodPolicy policy, CreatureSubjectResolver resolver, IReadOnlyList<GoalSet> goalsBySpecies, IRandom random)
    : ISimulationSystem
{
    private readonly ComponentStore<Combatant> _combatants = world.Store<Combatant>();

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
            int health = HealthInput.Of(_combatants, entities[i]);
            var context = new DecisionContext(goals, tick.Index, current[i].Goal, resolver.FocusSubject(focus), focus.Distance, health);
            current[i] = new CurrentGoal(evaluator.Decide(_minds.Get(entities[i]).Handle, context, random).Goal);
        }
    }
}

/// <summary>
/// Behaviour trees (AI Bible §7.3) at each creature's tier rate, with its current focus; paused while the creature answers a Pact
/// offer (<see cref="Bonding.PactAnswerSystem"/>).
/// </summary>
public sealed class BehaviourSystem(IEcsWorld world, IBehaviourTreeRunner runner, IAiLodPolicy policy, CreatureSubjectResolver resolver) : ISimulationSystem
{
    private readonly ComponentStore<Combatant> _combatants = world.Store<Combatant>();

    private readonly ComponentStore<CurrentGoal> _goals = world.Store<CurrentGoal>();
    private readonly ComponentStore<AiTier> _tiers = world.Store<AiTier>();
    private readonly ComponentStore<FocusRef> _focus = world.Store<FocusRef>();
    private readonly ComponentStore<MindRef> _minds = world.Store<MindRef>();
    private readonly ComponentStore<Bonding.PactAnswer> _answers = world.Store<Bonding.PactAnswer>();

    /// <inheritdoc />
    public SimulationStage Stage => SimulationStage.Behaviour;

    /// <inheritdoc />
    public void Execute(in SimulationTick tick)
    {
        ReadOnlySpan<Entity> entities = _goals.Entities;
        Span<CurrentGoal> goals = _goals.Components;
        for (int i = 0; i < entities.Length; i++)
        {
            if (!policy.IsDue(_tiers.Get(entities[i]).Tier, AiActivity.Behaviour, entities[i].Index, tick.Index) || _answers.Has(entities[i]))
            {
                continue;
            }

            FocusRef focus = _focus.Get(entities[i]);
            var context = new BehaviourContext(entities[i], goals[i].Goal, tick.Index, resolver.FocusSubject(focus), focus.Distance, HealthInput.Of(_combatants, entities[i]));
            runner.Tick(_minds.Get(entities[i]).Handle, context);
        }
    }
}

/// <summary>
/// Carries out creature intents from the behaviour trees (AI Bible §7.3): MoveTo picks a wander point 10–40 m away, the focus or a
/// point away from it (staying inside the world bounds) and asks the <see cref="PathPlanner"/> for a path (toward the focus it stops
/// <see cref="ApproachGapMetres"/> short of touching, by both bodies' sizes); the creature waits until
/// the path arrives at the next tick (TDD §2.4). Stop halts; Interact satisfies its need; Emote shows the tree's emote when the
/// appraisal of the focus has none (perception sets that first, AI Bible §6.2). Player
/// intents arrive through the authoritative intent pipeline instead (TDD §3.2).
/// </summary>
public sealed class CreatureIntentSystem(
    IEcsWorld world,
    IEventBus events,
    IBehaviourTreeRunner runner,
    ICreatureMindStore minds,
    IRandom random,
    PathPlanner paths,
    IJobScheduler jobs,
    BodySizes sizes,
    float walkSpeed,
    Vector3 boundsMin,
    Vector3 boundsMax)
    : ISimulationSystem
{
    /// <summary>Space left between bodies when a creature walks up to its focus.</summary>
    public const float ApproachGapMetres = 0.5f;

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
    private readonly ComponentStore<Snared> _snared = world.Store<Snared>();

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
                case IntentKind.MoveTo when _snared.Has(intent.Actor):
                    runner.ReportOutcome(mind, succeeded: false);
                    break;
                case IntentKind.MoveTo:
                    if (TryResolveTarget(intent.Actor, intent.Target, out Vector3 target)
                        && paths.Request(intent.Actor, _positions.Get(intent.Actor).Value, target, walkSpeed * intent.Speed / Percent))
                    {
                        _targets.Get(intent.Actor) = _targets.Get(intent.Actor) with { Active = 0 };
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
                case IntentKind.Emote when !_snared.Has(intent.Actor) && _emotes.Get(intent.Actor).Value == Emote.None:
                    _emotes.Get(intent.Actor) = new CurrentEmote(intent.Emote);
                    break;
                default:
                    break;
            }
        }

        paths.Submit(jobs);
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
                Vector3 toFocus = _positions.Get(focus.Entity).Value - position;
                toFocus.Y = 0;
                float gap = toFocus.Length();
                float stop = sizes.RadiusOf(actor) + sizes.RadiusOf(focus.Entity) + ApproachGapMetres;
                target = gap > stop ? position + (toFocus * ((gap - stop) / gap)) : position;
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

/// <summary>
/// Moves entities along their paths (<see cref="IPathSink"/>: waypoint by waypoint, distances measured on the ground plane), keeps them
/// on the terrain (<see cref="Heightfield"/>; flat at their own height without one), turns them to face the way they go, updates the
/// spatial index and reports arrival at the last waypoint, or failure when no path was found.
/// </summary>
public sealed class MovementSystem(IEcsWorld world, ISpatialIndex grid, IBehaviourTreeRunner runner, Heightfield? ground, int entityCapacity)
    : ISimulationSystem, IPathSink
{
    private readonly Vector3[] _paths = new Vector3[entityCapacity * PathPlanner.MaxWaypoints];
    private readonly byte[] _pathCounts = new byte[entityCapacity];
    private readonly byte[] _pathNext = new byte[entityCapacity];
    private readonly ComponentStore<MoveTarget> _targets = world.Store<MoveTarget>();
    private readonly ComponentStore<Position> _positions = world.Store<Position>();
    private readonly ComponentStore<Facing> _facings = world.Store<Facing>();
    private readonly ComponentStore<MindRef> _minds = world.Store<MindRef>();
    private readonly ComponentStore<Snared> _snared = world.Store<Snared>();

    /// <inheritdoc />
    public SimulationStage Stage => SimulationStage.Movement;

    /// <inheritdoc />
    public void Execute(in SimulationTick tick)
    {
        ReadOnlySpan<Entity> entities = _targets.Entities;
        Span<MoveTarget> targets = _targets.Components;
        for (int i = 0; i < entities.Length; i++)
        {
            if (targets[i].Active == 0 || _snared.Has(entities[i]))
            {
                continue;
            }

            ref Position position = ref _positions.Get(entities[i]);
            Vector3 toTarget = targets[i].Target - position.Value;
            toTarget.Y = 0;
            float distance = toTarget.Length();
            if (distance <= targets[i].Speed)
            {
                position = new Position(OnGround(targets[i].Target with { Y = position.Value.Y }));
                int index = entities[i].Index;
                if (_pathNext[index] < _pathCounts[index])
                {
                    targets[i] = targets[i] with { Target = _paths[(index * PathPlanner.MaxWaypoints) + _pathNext[index]++] };
                }
                else
                {
                    targets[i] = targets[i] with { Active = 0 };
                    Report(entities[i], succeeded: true);
                }
            }
            else
            {
                Vector3 direction = toTarget / distance;
                position = new Position(OnGround(position.Value + (direction * targets[i].Speed)));
                _facings.Get(entities[i]) = new Facing(direction);
            }

            grid.Move(entities[i], position.Value);
        }
    }

    /// <inheritdoc />
    public void OnPath(Entity entity, PathStatus status, ReadOnlySpan<Vector3> waypoints, float speed)
    {
        if (status == PathStatus.NotFound || waypoints.IsEmpty || !_targets.Has(entity))
        {
            Report(entity, succeeded: false);
            return;
        }

        int index = entity.Index;
        waypoints.CopyTo(_paths.AsSpan(index * PathPlanner.MaxWaypoints, PathPlanner.MaxWaypoints));
        _pathCounts[index] = (byte)waypoints.Length;
        _pathNext[index] = 1;
        _targets.Get(entity) = new MoveTarget(waypoints[0], speed, 1);
    }

    private Vector3 OnGround(Vector3 position) => ground is null ? position : position with { Y = ground.HeightAt(position.X, position.Z) };

    private void Report(Entity entity, bool succeeded)
    {
        if (_minds.Has(entity))
        {
            runner.ReportOutcome(_minds.Get(entity).Handle, succeeded);
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
