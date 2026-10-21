using System.Numerics;
using SumonerWorld.Core.Cognition.Behaviour;
using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.World;

namespace SumonerWorld.Core.Bonding;

/// <summary>What a summoned companion is doing (M2-07).</summary>
public enum CompanionMode
{
    /// <summary>Staying with its summoner.</summary>
    Follow = 0,

    /// <summary>Fighting the foe (defending its summoner, or helping them).</summary>
    Fight = 1,

    /// <summary>Bored and independent: its own mind (the behaviour trees) has it wander nearby for a while.</summary>
    Roam = 2,
}

/// <summary>
/// A summoned companion's state (M2-07): its foe and how long it stays one, since when its summoner has stood still, where the
/// summoner was, when it may ask for a new path, and its mode. Laid out without padding (hashed as raw bytes).
/// </summary>
/// <param name="Foe">The creature it fights (valid while <paramref name="FoeUntil"/> is in the future).</param>
/// <param name="FoeUntil">Tick the foe is forgotten unless it strikes again.</param>
/// <param name="OwnerStillSince">Tick the summoner last moved.</param>
/// <param name="NextPathTick">Earliest tick for a new path request.</param>
/// <param name="OwnerAt">Where the summoner was last tick.</param>
/// <param name="Mode">A <see cref="CompanionMode"/>.</param>
public record struct CompanionState(Entity Foe, ulong FoeUntil, ulong OwnerStillSince, ulong NextPathTick, Vector3 OwnerAt, int Mode);

/// <summary>
/// Watches the hits of each tick for summoned companions (M2-07, GDD §10): when something hits a companion's summoner, or the
/// companion, it becomes the foe (defending unprompted); when the summoner hits a creature, that one does (helping in the fight).
/// The summoner's own other companions are never foes. Runs after <see cref="CombatSystem"/>, which publishes the hits.
/// </summary>
public sealed class CompanionAlertSystem : ISimulationSystem
{
    /// <summary>How long a foe stays one without a new hit (10 s).</summary>
    public const ulong FoeTicks = 300;

    private readonly CreatureWorld _world;
    private readonly ComponentStore<CompanionState> _companions;
    private readonly ComponentStore<PlayerAvatar> _players;
    private readonly ComponentStore<MindRef> _minds;

    /// <summary>Creates the system for a world.</summary>
    public CompanionAlertSystem(CreatureWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
        _companions = world.World.Store<CompanionState>();
        _players = world.World.Store<PlayerAvatar>();
        _minds = world.World.Store<MindRef>();
    }

    /// <inheritdoc />
    public SimulationStage Stage => SimulationStage.Combat;

    /// <inheritdoc />
    public void Execute(in SimulationTick tick)
    {
        foreach (CombatHit hit in _world.Events.Read<CombatHit>())
        {
            ReadOnlySpan<Entity> companions = _companions.Entities;
            for (int i = 0; i < companions.Length; i++)
            {
                Entity companion = companions[i];
                ulong owner = _world.Services.Bonding.GetBond(_minds.Get(companion).Handle).Owner;
                Entity? foe = hit.Target == companion || IsOwner(hit.Target, owner) ? hit.Attacker
                    : IsOwner(hit.Attacker, owner) ? hit.Target
                    : null;
                if (foe is Entity found && found != companion && _minds.Has(found) && !IsCompanionOf(found, owner))
                {
                    ref CompanionState state = ref _companions.Get(companion);
                    state.Foe = found;
                    state.FoeUntil = tick.Index + FoeTicks;
                }
            }
        }
    }

    private bool IsOwner(Entity entity, ulong account) => _players.Has(entity) && _players.Get(entity).AccountId == account;

    private bool IsCompanionOf(Entity creature, ulong account)
    {
        BondState bond = _world.Services.Bonding.GetBond(_minds.Get(creature).Handle);
        return bond.State == OwnershipState.Pacted && bond.Owner == account;
    }
}

/// <summary>
/// Summoned companions' behaviour (M2-07, GDD §10, AI Bible §10.4), in place of the behaviour trees while it applies:
/// <list type="bullet">
/// <item><b>Follow:</b> it keeps within <see cref="FollowMetres"/> of its summoner (body edges), running to catch up when far
/// behind, and stops at <see cref="ComfortMetres"/>, off to the side: it does not crowd them. Left more than
/// <see cref="CatchUpMetres"/> behind (a cliff, a fast run), it is drawn back to their side as when summoned.</item>
/// <item><b>Fight:</b> with a foe (<see cref="CompanionAlertSystem"/>) that still stands, it goes for it and attacks with its
/// species' moves; it lets go when the foe yields or is down (the summoner's spare is respected), the time runs out, or the
/// fight would take it more than <see cref="LeashMetres"/> from its summoner.</item>
/// <item><b>Roam:</b> an independent companion (Loyalty below <see cref="IndependentBelow"/>) gets bored when its summoner stands
/// still for <see cref="BoredAfterTicks"/>: its own mind takes over and it wanders, until it is <see cref="LeashMetres"/> away
/// or the summoner moves on.</item>
/// </list>
/// A companion never attacks its own summoner (<see cref="CreatureAttackSystem"/> checks it). A companion whose summoner is not in
/// the world lives by its own mind.
/// </summary>
public sealed class CompanionSystem : ISimulationSystem
{
    /// <summary>Farthest a following companion lets itself fall behind before walking after its summoner (body edges, metres).</summary>
    public const float FollowMetres = 4f;

    /// <summary>Where it stops when it caught up (body edges, metres): its personal space.</summary>
    public const float ComfortMetres = 1.5f;

    /// <summary>Beyond this it runs.</summary>
    public const float RunMetres = 10f;

    /// <summary>Beyond this it is drawn back to its summoner's side.</summary>
    public const float CatchUpMetres = 60f;

    /// <summary>A fight or a roam never takes it farther than this from its summoner.</summary>
    public const float LeashMetres = 20f;

    /// <summary>Loyalty below this makes a companion independent: it roams when bored.</summary>
    public const int IndependentBelow = 0;

    /// <summary>How long the summoner must stand still before an independent companion gets bored (10 s).</summary>
    public const ulong BoredAfterTicks = 300;

    private const ulong RepathTicks = 10;
    private const float WalkPercent = 120;
    private const float RunPercent = 230;
    private const float Percent = 100;
    private const float MovedMetres = 0.05f;
    private const float AttackReachMetres = 2f;

    private readonly CreatureWorld _world;
    private readonly EcsWorld _ecs;
    private readonly ComponentStore<CompanionState> _companions;
    private readonly ComponentStore<MindRef> _minds;
    private readonly ComponentStore<PlayerAvatar> _players;
    private readonly ComponentStore<Position> _positions;
    private readonly ComponentStore<MoveTarget> _targets;
    private readonly ComponentStore<FocusRef> _focus;
    private readonly ComponentStore<Combatant> _combatants;
    private readonly List<Entity> _gone = [];

    /// <summary>Creates the system for a world.</summary>
    public CompanionSystem(CreatureWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
        _ecs = world.World;
        _companions = _ecs.Store<CompanionState>();
        _minds = _ecs.Store<MindRef>();
        _players = _ecs.Store<PlayerAvatar>();
        _positions = _ecs.Store<Position>();
        _targets = _ecs.Store<MoveTarget>();
        _focus = _ecs.Store<FocusRef>();
        _combatants = _ecs.Store<Combatant>();
    }

    /// <inheritdoc />
    public SimulationStage Stage => SimulationStage.Behaviour;

    /// <inheritdoc />
    public void Execute(in SimulationTick tick)
    {
        Track(tick.Index);
        ReadOnlySpan<Entity> companions = _companions.Entities;
        for (int i = 0; i < companions.Length; i++)
        {
            Entity companion = companions[i];
            if (!_ecs.Has<PactAnswer>(companion) && FindOwner(companion) is Entity owner)
            {
                Behave(companion, owner, ref _companions.Get(companion), tick.Index);
            }
        }
    }

    // Summoned creatures get a state; ones that were dismissed (or left) lose it.
    private void Track(ulong now)
    {
        ReadOnlySpan<Entity> creatures = _minds.Entities;
        for (int i = 0; i < creatures.Length; i++)
        {
            bool summoned = _world.Services.Bonding.GetBond(_minds.Components[i].Handle).Summoned;
            if (summoned && !_companions.Has(creatures[i]))
            {
                _ecs.Add(creatures[i], new CompanionState(default, 0, now, 0, Vector3.Zero, (int)CompanionMode.Follow));
            }
        }

        foreach (Entity companion in _companions.Entities)
        {
            if (!_world.Services.Bonding.GetBond(_minds.Get(companion).Handle).Summoned)
            {
                _gone.Add(companion);
            }
        }

        foreach (Entity companion in _gone)
        {
            _ecs.Remove<CompanionState>(companion);
        }

        _gone.Clear();
    }

    private Entity? FindOwner(Entity companion)
    {
        ulong account = _world.Services.Bonding.GetBond(_minds.Get(companion).Handle).Owner;
        ReadOnlySpan<Entity> players = _players.Entities;
        for (int i = 0; i < players.Length; i++)
        {
            if (_players.Components[i].AccountId == account)
            {
                return players[i];
            }
        }

        return null;
    }

    private void Behave(Entity companion, Entity owner, ref CompanionState state, ulong now)
    {
        Vector3 me = _positions.Get(companion).Value;
        Vector3 them = _positions.Get(owner).Value;
        if (Flat(them - state.OwnerAt) > MovedMetres)
        {
            state.OwnerStillSince = now;
        }

        state.OwnerAt = them;
        float gap = Flat(them - me) - _world.Sizes.RadiusOf(companion) - _world.Sizes.RadiusOf(owner);
        if (StillFoe(ref state, them, now))
        {
            state.Mode = (int)CompanionMode.Fight;
            Fight(companion, ref state, me, now);
            return;
        }

        if (gap > CatchUpMetres)
        {
            SummoningSystem.PlaceBeside(_world, owner, companion);
            state.Mode = (int)CompanionMode.Follow;
            return;
        }

        bool bored = now - state.OwnerStillSince >= BoredAfterTicks && Independent(companion);
        if (bored && gap < LeashMetres)
        {
            state.Mode = (int)CompanionMode.Roam;
            return;
        }

        state.Mode = (int)CompanionMode.Follow;
        Follow(companion, owner, ref state, me, them, gap, now);
    }

    private bool StillFoe(ref CompanionState state, Vector3 owner, ulong now)
    {
        Entity foe = state.Foe;
        bool standing = now < state.FoeUntil && _ecs.IsAlive(foe) && _positions.Has(foe)
            && !_ecs.Has<Yielding>(foe) && !_ecs.Has<Downed>(foe) && (!_combatants.Has(foe) || _combatants.Get(foe).Health > 0)
            && Flat(_positions.Get(foe).Value - owner) <= LeashMetres;
        if (!standing)
        {
            state.FoeUntil = 0;
        }

        return standing;
    }

    private void Fight(Entity companion, ref CompanionState state, Vector3 me, ulong now)
    {
        Vector3 foe = _positions.Get(state.Foe).Value;
        float gap = Flat(foe - me) - _world.Sizes.RadiusOf(companion) - _world.Sizes.RadiusOf(state.Foe);
        _focus.Get(companion) = new FocusRef(state.Foe, 0, 1);
        if (gap <= AttackReachMetres)
        {
            _world.Events.Publish(new CreatureIntent(companion, IntentKind.Attack, MoveTargetMode.Focus, 0, Cognition.Appraisal.Emote.None, InteractVerb.Eat, null));
            return;
        }

        if (now >= state.NextPathTick)
        {
            Vector3 towards = Vector3.Normalize(new Vector3(foe.X - me.X, 0, foe.Z - me.Z));
            Walk(companion, me, foe - (towards * (_world.Sizes.RadiusOf(companion) + _world.Sizes.RadiusOf(state.Foe) + 0.5f)), RunPercent, ref state, now);
        }
    }

    // Toward a spot beside and a little behind the summoner, at the comfort distance; stopped once there.
    private void Follow(Entity companion, Entity owner, ref CompanionState state, Vector3 me, Vector3 them, float gap, ulong now)
    {
        MoveTarget target = _targets.Get(companion);
        if (gap <= ComfortMetres + 0.5f && target.Active != 0)
        {
            _targets.Get(companion) = target with { Active = 0 };
            return;
        }

        if (gap <= FollowMetres && target.Active == 0)
        {
            return;
        }

        if (gap > ComfortMetres + 0.5f && now >= state.NextPathTick)
        {
            Vector3 away = new Vector3(me.X - them.X, 0, me.Z - them.Z);
            away = away.LengthSquared() > 0 ? Vector3.Normalize(away) : Vector3.UnitX;
            float stop = _world.Sizes.RadiusOf(companion) + _world.Sizes.RadiusOf(owner) + ComfortMetres;
            Walk(companion, me, them + (away * stop), gap > RunMetres ? RunPercent : WalkPercent, ref state, now);
        }
    }

    private void Walk(Entity companion, Vector3 from, Vector3 to, float percent, ref CompanionState state, ulong now)
    {
        to = Vector3.Clamp(to, _world.Settings.BoundsMin, _world.Settings.BoundsMax);
        if (_world.Paths.Request(companion, from, to, _world.Settings.WalkSpeed * percent / Percent))
        {
            _targets.Get(companion) = _targets.Get(companion) with { Active = 0 };
            state.NextPathTick = now + RepathTicks;
        }
    }

    private bool Independent(Entity companion) =>
        _world.Minds.GetPersonality(_minds.Get(companion).Handle).Loyalty < IndependentBelow;

    private static float Flat(Vector3 offset) => new Vector2(offset.X, offset.Z).Length();
}
