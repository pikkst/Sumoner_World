using System.Numerics;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.World;

namespace SumonerWorld.Core.Combat;

/// <summary>
/// Stage 7, combat (combat spec §4): advances every move in progress and, during its active ticks, hits each body inside its
/// shape once. Damage follows <see cref="CombatRules.Damage"/>. A player's hit leaves <c>AttackedBy</c> in the creature's mind
/// (<c>Mistreated</c> if it had yielded); a wild creature pushed below the yield threshold may yield (§6). Nothing is destroyed
/// here: defeats are settled in <see cref="VitalsSystem"/>.
/// </summary>
public sealed class CombatSystem : ISimulationSystem
{
    /// <summary>Most targets one move can hit.</summary>
    public const int MaxHitsPerMove = 8;

    private const int MaxCandidates = 64;
    private const float CentimetresPerMetre = 100f;
    private const int PerMille = 1000;

    private readonly CreatureWorld _world;
    private readonly EcsWorld _ecs;
    private readonly ComponentStore<ActiveMove> _moves;
    private readonly ComponentStore<Combatant> _combatants;
    private readonly ComponentStore<Position> _positions;
    private readonly ComponentStore<PlayerAvatar> _players;
    private readonly ComponentStore<GroupRef> _groups;
    private readonly ComponentStore<Yielding> _yielding;
    private readonly Entity[] _hits;
    private readonly Entity[] _candidates = new Entity[MaxCandidates];

    /// <summary>Creates the system for a world.</summary>
    public CombatSystem(CreatureWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
        _ecs = world.World;
        _moves = _ecs.Store<ActiveMove>();
        _combatants = _ecs.Store<Combatant>();
        _positions = _ecs.Store<Position>();
        _players = _ecs.Store<PlayerAvatar>();
        _groups = _ecs.Store<GroupRef>();
        _yielding = _ecs.Store<Yielding>();
        _hits = new Entity[world.Settings.EntityCapacity * MaxHitsPerMove];
    }

    /// <inheritdoc />
    public SimulationStage Stage => SimulationStage.Combat;

    /// <summary>True when a body of radius <paramref name="radius"/> at <paramref name="target"/> is inside the move's shape.</summary>
    public static bool InShape(MoveShapeDefinition shape, Vector3 attacker, Vector2 aim, Vector3 target, float radius)
    {
        ArgumentNullException.ThrowIfNull(shape);
        float reach = shape.ReachCm / CentimetresPerMetre;
        var offset = new Vector2(target.X - attacker.X, target.Z - attacker.Z);
        float distance = offset.Length();
        switch (shape.Kind)
        {
            case MoveShapeKind.Arc:
                if (distance - radius > reach)
                {
                    return false;
                }

                return distance <= radius || Vector2.Dot(offset / distance, aim) >= MathF.Cos(shape.AngleDeg * MathF.PI / 360f);
            case MoveShapeKind.Line:
                float along = Vector2.Dot(offset, aim);
                float across = MathF.Abs((offset.X * aim.Y) - (offset.Y * aim.X));
                return along >= -radius && along - radius <= reach && across <= (shape.WidthCm / CentimetresPerMetre / 2f) + radius;
            default:
                return distance - radius <= reach;
        }
    }

    /// <inheritdoc />
    public void Execute(in SimulationTick tick)
    {
        ulong now = tick.Index;
        ReadOnlySpan<Entity> attackers = _moves.Entities;
        for (int i = attackers.Length - 1; i >= 0; i--)
        {
            Entity attacker = attackers[i];
            ref ActiveMove move = ref _moves.Components[i];
            MoveDefinition definition = _world.Data.Moves[move.Move];
            ulong age = now - move.StartTick;
            if (age >= (ulong)definition.TotalTicks)
            {
                _ecs.Remove<ActiveMove>(attacker);
                continue;
            }

            bool active = age >= (ulong)definition.WindupTicks && age < (ulong)(definition.WindupTicks + definition.ActiveTicks);
            if (active && _combatants.Get(attacker).Health > 0 && !_yielding.Has(attacker))
            {
                Resolve(attacker, ref move, definition, now);
            }
        }
    }

    private void Resolve(Entity attacker, ref ActiveMove move, MoveDefinition definition, ulong now)
    {
        Vector3 at = _positions.Get(attacker).Value;
        var aim = new Vector2(move.AimX, move.AimZ);
        float reach = definition.Shape.ReachCm / CentimetresPerMetre;
        int found = _world.Grid.QueryRadius(at, reach + _world.Sizes.Largest, _candidates).Count;
        for (int n = 0; n < found && move.Hits < MaxHitsPerMove; n++)
        {
            Entity target = _candidates[n];
            if (target == attacker || !_combatants.Has(target) || _combatants.Get(target).Health == 0 || Friendly(attacker, target)
                || AlreadyHit(attacker, move.Hits, target)
                || !InShape(definition.Shape, at, aim, _positions.Get(target).Value, _world.Sizes.RadiusOf(target)))
            {
                continue;
            }

            _hits[(attacker.Index * MaxHitsPerMove) + move.Hits] = target;
            move.Hits++;
            Hit(attacker, target, move.Move, definition, now);
        }
    }

    private void Hit(Entity attacker, Entity target, int moveIndex, MoveDefinition definition, ulong now)
    {
        ref Combatant victim = ref _combatants.Get(target);
        int damage = CombatRules.Damage(definition.Damage, _combatants.Get(attacker).Power, CombatRules.ResistTo(victim, definition.DamageType), victim.Guard);
        damage = Math.Min(damage, victim.Health);
        victim.Health -= damage;
        victim.LastHurtTick = now;
        ulong account = _players.Has(attacker) ? _players.Get(attacker).AccountId : 0;
        victim.LastAttacker = account;
        _world.Events.Publish(new CombatHit(attacker, target, moveIndex, damage, victim.Health));
        if (!_ecs.Has<MindRef>(target))
        {
            return;
        }

        bool yielded = _yielding.Has(target);
        if (account != 0)
        {
            PlayerInteractions.Remember(_world, target, yielded ? "Mistreated" : "AttackedBy", account, now);
            if (!yielded)
            {
                PlayerInteractions.Startle(_world, target, PlayerInteractions.EmotionNudge, now);
            }
        }

        if (yielded)
        {
            _yielding.Get(target) = _yielding.Get(target) with { LastHurt = now, Account = account };
        }
        else if (victim.Health > 0 && ShouldYield(target, victim))
        {
            _ecs.Add(target, new Yielding(now, now, account));
            ref MoveTarget walking = ref _ecs.GetComponent<MoveTarget>(target);
            walking = walking with { Active = 0 };
        }
    }

    private bool ShouldYield(Entity creature, in Combatant combatant)
    {
        CombatDefinition rules = _world.Data.Combat;
        return (long)combatant.Health * PerMille < (long)combatant.MaxHealth * rules.YieldBelowPerMille
            && _world.Minds.GetPersonality(_ecs.GetComponent<MindRef>(creature).Handle).Boldness <= rules.YieldMaxBoldness;
    }

    // PvE: players do not hurt players; a herd or pack does not hurt its own members (companions come with M2-07).
    private bool Friendly(Entity attacker, Entity target) =>
        (_players.Has(attacker) && _players.Has(target))
        || (_groups.Has(attacker) && _groups.Has(target) && _groups.Get(attacker).Group == _groups.Get(target).Group);

    private bool AlreadyHit(Entity attacker, int hits, Entity target)
    {
        int start = attacker.Index * MaxHitsPerMove;
        for (int i = 0; i < hits; i++)
        {
            if (_hits[start + i] == target)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Stage 8, status effects and death (combat spec §5, §6): regenerates stamina, mana and health without rounding drift, lets a
/// yielded creature that was left alone go free as spared (<c>DefeatedButSpared</c>), puts a defeated player down and back up,
/// and removes defeated wild creatures (their kin remember a player's kill as <c>KilledKin</c>).
/// </summary>
public sealed class VitalsSystem : ISimulationSystem
{
    private readonly CreatureWorld _world;
    private readonly EcsWorld _ecs;
    private readonly ComponentStore<Combatant> _combatants;
    private readonly ComponentStore<Yielding> _yielding;
    private readonly ComponentStore<Downed> _downed;
    private readonly ComponentStore<PlayerAvatar> _players;
    private readonly Entity[] _defeated;
    private readonly int _ticksPerSecond;

    /// <summary>Creates the system for a world.</summary>
    public VitalsSystem(CreatureWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
        _ecs = world.World;
        _combatants = _ecs.Store<Combatant>();
        _yielding = _ecs.Store<Yielding>();
        _downed = _ecs.Store<Downed>();
        _players = _ecs.Store<PlayerAvatar>();
        _defeated = new Entity[world.Settings.EntityCapacity];
        _ticksPerSecond = world.Clock.TicksPerSecond;
    }

    /// <inheritdoc />
    public SimulationStage Stage => SimulationStage.StatusEffects;

    /// <summary>
    /// What a rate of <paramref name="perSecond"/> adds on the <paramref name="tick"/>th tick (1-based) of regeneration: the
    /// per-tick shares add up to exactly the rate every second, in integers.
    /// </summary>
    public static int Gain(long perSecond, ulong tick, int ticksPerSecond) =>
        tick == 0 ? 0 : (int)((perSecond * (long)tick / ticksPerSecond) - (perSecond * ((long)tick - 1) / ticksPerSecond));

    /// <inheritdoc />
    public void Execute(in SimulationTick tick)
    {
        ulong now = tick.Index;
        CombatDefinition rules = _world.Data.Combat;
        int defeated = 0;
        ReadOnlySpan<Entity> entities = _combatants.Entities;
        Span<Combatant> combatants = _combatants.Components;
        for (int i = 0; i < entities.Length; i++)
        {
            ref Combatant c = ref combatants[i];
            if (c.Health == 0)
            {
                if (_players.Has(entities[i]))
                {
                    GetUp(entities[i], ref c, rules, now);
                }
                else
                {
                    _defeated[defeated++] = entities[i];
                }

                continue;
            }

            Regenerate(ref c, rules, now);
        }

        Spare(rules, now);
        for (int i = 0; i < defeated; i++)
        {
            Defeat(_defeated[i], now);
        }
    }

    private void Regenerate(ref Combatant c, CombatDefinition rules, ulong now)
    {
        ulong staminaFrom = c.LastSpendTick + (ulong)rules.StaminaRegenDelayTicks;
        if (c.Stamina < c.MaxStamina && now > staminaFrom)
        {
            c.Stamina = Math.Min(c.MaxStamina, c.Stamina + Gain(rules.StaminaRegenPerSecond, now - staminaFrom, _ticksPerSecond));
        }

        if (c.Mana < c.MaxMana)
        {
            c.Mana = Math.Min(c.MaxMana, c.Mana + Gain(rules.ManaRegenPerSecond, now, _ticksPerSecond));
        }

        ulong healthFrom = c.LastHurtTick + (ulong)rules.HealthRegenDelayTicks;
        if (c.Health < c.MaxHealth && now > healthFrom)
        {
            long perSecond = (long)c.MaxHealth * rules.HealthRegenPerMillePerSecond / 1000;
            c.Health = Math.Min(c.MaxHealth, c.Health + Gain(perSecond, now - healthFrom, _ticksPerSecond));
        }
    }

    private void GetUp(Entity player, ref Combatant c, CombatDefinition rules, ulong now)
    {
        if (!_downed.Has(player))
        {
            _ecs.Add(player, new Downed(now));
            _world.Events.Publish(new CombatantDefeated(player, ushort.MaxValue, c.LastAttacker, 0));
            return;
        }

        if (now - _downed.Get(player).Since >= (ulong)rules.PlayerDownTicks)
        {
            c = c with { Health = c.MaxHealth, Stamina = c.MaxStamina, Mana = c.MaxMana, LastHurtTick = now };
            _ecs.Remove<Downed>(player);
        }
    }

    private void Spare(CombatDefinition rules, ulong now)
    {
        ReadOnlySpan<Entity> yielded = _yielding.Entities;
        for (int i = yielded.Length - 1; i >= 0; i--)
        {
            Entity creature = yielded[i];
            Yielding state = _yielding.Components[i];
            if (now - state.LastHurt < (ulong)rules.SpareAfterTicks || _combatants.Get(creature).Health == 0)
            {
                continue;
            }

            if (state.Account != 0)
            {
                PlayerInteractions.Remember(_world, creature, "DefeatedButSpared", state.Account, now);
            }

            _ecs.Remove<Yielding>(creature);
        }
    }

    private void Defeat(Entity creature, ulong now)
    {
        ushort species = _ecs.GetComponent<SpeciesRef>(creature).Index;
        ulong account = _combatants.Get(creature).LastAttacker;
        int witnesses = account != 0 ? _world.WitnessKill(creature, species, account, now) : 0;
        _world.Events.Publish(new CombatantDefeated(creature, species, account, witnesses));
        _world.DespawnCreature(creature);
    }
}
