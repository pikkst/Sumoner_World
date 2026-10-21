using System.Numerics;
using SumonerWorld.Core.Cognition.Behaviour;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.World;

namespace SumonerWorld.Core.Combat;

/// <summary>
/// Carries out the behaviour trees' <c>attack</c> intents (AI Bible §7.3, combat spec §9, M2-03): the creature turns to its focus
/// and starts the first move whose shape reaches it and that it can pay for: the moves of its active skills first (M2-10), then its
/// species' moves (so a bogfox bites up close and pounces from farther away),
/// through the same <see cref="CombatRules"/> as players. Nothing happens when no move reaches, the focus is already down, the
/// creature is snared, the focus is the summoner it is pacted to, or <see cref="CombatRules.CanStart"/> refuses (in a move, tired, staggered, yielding); the tree simply tries
/// again on its next cycle.
/// </summary>
public sealed class CreatureAttackSystem : ISimulationSystem
{
    private readonly CreatureWorld _world;
    private readonly EcsWorld _ecs;
    private readonly ComponentStore<FocusRef> _focus;
    private readonly ComponentStore<Position> _positions;
    private readonly ComponentStore<Combatant> _combatants;
    private readonly ComponentStore<Snared> _snared;
    private readonly ComponentStore<PlayerAvatar> _players;
    private readonly ComponentStore<MindRef> _minds;
    private readonly int[][] _movesBySource;
    private readonly int[] _skillMoves = new int[Progression.SkillRules.Slots(Progression.LifeStage.Elder)];
    private readonly ComponentStore<Progression.CreatureSkills> _skillsStore;
    private readonly ComponentStore<Progression.CreatureGrowth> _growthStore;
    private readonly ComponentStore<CreatureCombos> _combosStore;

    /// <summary>Creates the system for a world.</summary>
    public CreatureAttackSystem(CreatureWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
        _ecs = world.World;
        _focus = _ecs.Store<FocusRef>();
        _positions = _ecs.Store<Position>();
        _combatants = _ecs.Store<Combatant>();
        _snared = _ecs.Store<Snared>();
        _players = _ecs.Store<PlayerAvatar>();
        _minds = _ecs.Store<MindRef>();
        _skillsStore = _ecs.Store<Progression.CreatureSkills>();
        _growthStore = _ecs.Store<Progression.CreatureGrowth>();
        _combosStore = _ecs.Store<CreatureCombos>();
        _movesBySource = [.. world.Data.Species.Select(species => species.Combat.Moves.Select(world.Data.MoveIndex).ToArray())];
    }

    /// <summary>Attacks started since the start.</summary>
    public int Started { get; private set; }

    /// <inheritdoc />
    public SimulationStage Stage => SimulationStage.Movement;

    /// <inheritdoc />
    public void Execute(in SimulationTick tick)
    {
        foreach (CreatureIntent intent in _world.Events.Read<CreatureIntent>())
        {
            if (intent.Kind == IntentKind.Attack)
            {
                TryAttack(intent.Actor, tick.Index);
            }
        }
    }

    // A move that makes a combo it knows on this target (it tries that first, M2-12), or −1.
    private int ComboMove(Entity actor, Entity target, ulong now, int skills, int[] species)
    {
        if (!_combosStore.Has(actor) || _combosStore.Get(actor).Learned == 0)
        {
            return -1;
        }

        for (int i = 0; i < skills + species.Length; i++)
        {
            int move = i < skills ? _skillMoves[i] : species[i - skills];
            int combo = ComboRules.Match(_world.Data, _world.Data.Moves[move], _ecs, target, now);
            if (combo >= 0 && _combosStore.Get(actor).Knows(combo))
            {
                return move;
            }
        }

        return -1;
    }

    // A companion never attacks the player it is pacted to (M2-07), whatever its own mind wants.
    private bool IsOwnSummoner(Entity actor, Entity target)
    {
        if (!_players.Has(target) || !_minds.Has(actor))
        {
            return false;
        }

        Bonding.BondState bond = _world.Services.Bonding.GetBond(_minds.Get(actor).Handle);
        return bond.State == Bonding.OwnershipState.Pacted && bond.Owner == _players.Get(target).AccountId;
    }

    private void TryAttack(Entity actor, ulong now)
    {
        FocusRef focus = _focus.Get(actor);
        if (focus.Has == 0 || _snared.Has(actor) || !_combatants.Has(actor) || !_positions.Has(focus.Entity)
            || !_combatants.Has(focus.Entity) || _combatants.Get(focus.Entity).Health == 0 || IsOwnSummoner(actor, focus.Entity))
        {
            return;
        }

        Vector3 from = _positions.Get(actor).Value;
        Vector3 to = _positions.Get(focus.Entity).Value;
        var flat = new Vector2(to.X - from.X, to.Z - from.Z);
        Vector2 aim = flat.LengthSquared() > 0 ? Vector2.Normalize(flat) : Vector2.UnitY;
        float radius = _world.Sizes.RadiusOf(focus.Entity);
        int source = _combatants.Get(actor).Source;
        int skills = _skillsStore.Has(actor) && _growthStore.Has(actor)
            ? Progression.SkillRules.ActiveMoves(
                _world.Data, _world.Data.SkillTreeOf(source), _skillsStore.Get(actor).Learned, (Progression.LifeStage)_growthStore.Get(actor).Stage, _skillMoves)
            : 0;
        int[] species = _movesBySource[source];
        int combo = ComboMove(actor, focus.Entity, now, skills, species);
        for (int i = -1; i < skills + species.Length; i++)
        {
            if (i < 0 && combo < 0)
            {
                continue;
            }

            int move = i < 0 ? combo : i < skills ? _skillMoves[i] : species[i - skills];
            MoveDefinition definition = _world.Data.Moves[move];
            if (!CombatSystem.InShape(definition.Shape, from, aim, to, radius) || !CombatRules.CanStart(_ecs, _world.Data, actor, move).IsApplied)
            {
                continue;
            }

            var direction = new Vector3(aim.X, 0, aim.Y);
            CombatRules.Start(_ecs, _world.Data, actor, move, direction, now);
            _ecs.GetComponent<Facing>(actor) = new Facing(direction);
            Started++;
            return;
        }
    }
}
