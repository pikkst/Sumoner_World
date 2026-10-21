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
    private const int Percent = 100;

    private readonly CreatureWorld _world;
    private readonly EcsWorld _ecs;
    private readonly ComponentStore<ActiveMove> _moves;
    private readonly ComponentStore<Combatant> _combatants;
    private readonly ComponentStore<Position> _positions;
    private readonly ComponentStore<PlayerAvatar> _players;
    private readonly ComponentStore<GroupRef> _groups;
    private readonly ComponentStore<Yielding> _yielding;
    private readonly ComponentStore<Blocking> _blocking;
    private readonly ComponentStore<Staggered> _staggered;
    private readonly ComponentStore<Facing> _facings;
    private readonly ComponentStore<MindRef> _minds;
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
        _blocking = _ecs.Store<Blocking>();
        _staggered = _ecs.Store<Staggered>();
        _facings = _ecs.Store<Facing>();
        _minds = _ecs.Store<MindRef>();
        _hits = new Entity[world.Settings.EntityCapacity * MaxHitsPerMove];
    }

    /// <inheritdoc />
    public SimulationStage Stage => SimulationStage.Combat;

    /// <summary>True when a body of radius <paramref name="radius"/> at <paramref name="target"/> is inside the move's shape.</summary>
    public static bool InShape(MoveShapeDefinition shape, Vector3 attacker, Vector2 aim, Vector3 target, float radius, float burstDistance = 0)
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
            case MoveShapeKind.Burst:
                Vector2 centre = aim * Math.Clamp(burstDistance, 0, reach);
                return Vector2.Distance(offset, centre) - radius <= shape.RadiusCm / CentimetresPerMetre;
            case MoveShapeKind.Ward:
                return false;
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
            if (active && _combatants.Get(attacker).Health > 0 && !_yielding.Has(attacker) && !_staggered.Has(attacker))
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
        if (definition.Shape.Kind == MoveShapeKind.Ward)
        {
            Ward(attacker, ref move, definition, now);
            return;
        }

        int found = _world.Grid.QueryRadius(at, reach + (definition.Shape.RadiusCm / CentimetresPerMetre) + _world.Sizes.Largest, _candidates).Count;
        for (int n = 0; n < found && move.Hits < MaxHitsPerMove; n++)
        {
            Entity target = _candidates[n];
            if (target == attacker || !_combatants.Has(target) || _combatants.Get(target).Health == 0 || Friendly(attacker, target)
                || AlreadyHit(attacker, move.Hits, target)
                || !InShape(definition.Shape, at, aim, _positions.Get(target).Value, _world.Sizes.RadiusOf(target), move.Distance))
            {
                continue;
            }

            // A dodge's invulnerable ticks let the move pass; it can still land later in its active part.
            if (CombatRules.IsEvading(_ecs, _world.Data, target, now))
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
        CombatDefinition rules = _world.Data.Combat;
        ref Combatant victim = ref _combatants.Get(target);
        bool guarded = _blocking.Has(target) && Faces(target, attacker, rules.BlockArcDeg);
        ulong account = _players.Has(attacker) ? _players.Get(attacker).AccountId : 0;
        if (guarded && now - _blocking.Get(target).Since <= (ulong)rules.ParryWindowTicks)
        {
            CombatRules.Stagger(_ecs, _world.Data, attacker, now);
            _world.Events.Publish(new CombatHit(attacker, target, moveIndex, 0, victim.Health, HitOutcome.Parried));
            return;
        }

        int damage = definition.Damage == 0 ? 0 : CombatRules.Damage(definition.Damage, _combatants.Get(attacker).Power, CombatRules.ResistTo(victim, definition.DamageType), victim.Guard);
        int combo = guarded ? -1 : ComboRules.Match(_world.Data, definition, _ecs, target, now);
        if (combo >= 0)
        {
            // A combo (combat spec §10): more damage, and the target is knocked down for longer.
            ComboDefinition rule = _world.Data.Combos[combo];
            damage = damage * rule.DamagePercent / Percent;
            CombatRules.Stagger(_ecs, _world.Data, target, now, rule.StaggerTicks);
            if (_world.Events.IsRegistered<ComboLanded>())
            {
                _world.Events.Publish(new ComboLanded(attacker, target, combo));
            }
        }

        if (definition.Effect is string effect && !guarded)
        {
            StatusRules.Apply(_ecs, _world.Data, target, effect, now);
        }

        if (guarded)
        {
            damage = damage * rules.BlockDamagePerMille / PerMille;
            victim.Stamina -= Math.Max(1, damage);
            victim.LastSpendTick = now;
            if (victim.Stamina <= 0)
            {
                victim.Stamina = 0;
                CombatRules.Stagger(_ecs, _world.Data, target, now); // guard broken
            }
        }
        else if (combo < 0 && definition.Poise > victim.Poise)
        {
            CombatRules.Stagger(_ecs, _world.Data, target, now);
        }

        damage = Math.Min(damage, victim.Health);
        victim.Health -= damage;
        victim.LastHurtTick = now;
        victim.LastAttacker = account;
        RememberFighter(target, account);

        _world.Events.Publish(new CombatHit(attacker, target, moveIndex, damage, victim.Health, guarded ? HitOutcome.Blocked : HitOutcome.Hit));
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

    // Everyone who fought it gets their own loot (crafting spec §4).
    private void RememberFighter(Entity target, ulong account)
    {
        if (account == 0)
        {
            return;
        }

        if (!_ecs.Has<Items.Fighters>(target))
        {
            _ecs.Add(target, default(Items.Fighters));
        }

        ref Items.Fighters fighters = ref _ecs.GetComponent<Items.Fighters>(target);
        fighters = fighters.With(account);
    }

    private bool ShouldYield(Entity creature, in Combatant combatant)
    {
        CombatDefinition rules = _world.Data.Combat;
        return (long)combatant.Health * PerMille < (long)combatant.MaxHealth * rules.YieldBelowPerMille
            && _world.Minds.GetPersonality(_ecs.GetComponent<MindRef>(creature).Handle).Boldness <= rules.YieldMaxBoldness;
    }

    // A block covers the arc in front of the blocker's facing.
    private bool Faces(Entity blocker, Entity attacker, int arcDeg)
    {
        Vector3 toAttacker = _positions.Get(attacker).Value - _positions.Get(blocker).Value;
        var flat = new Vector2(toAttacker.X, toAttacker.Z);
        if (flat.LengthSquared() == 0)
        {
            return true;
        }

        Vector3 facing = _facings.Get(blocker).Value;
        return Vector2.Dot(Vector2.Normalize(flat), Vector2.Normalize(new Vector2(facing.X, facing.Z))) >= MathF.Cos(arcDeg * MathF.PI / 360f);
    }

    // PvE: players do not hurt players; a herd or pack does not hurt its own members; a player does not hurt their own companions
    // (combat spec §4: area spells skip them too).
    private bool Friendly(Entity attacker, Entity target) =>
        (_players.Has(attacker) && _players.Has(target))
        || (_groups.Has(attacker) && _groups.Has(target) && _groups.Get(attacker).Group == _groups.Get(target).Group)
        || (_players.Has(attacker) && _minds.Has(target) && OwnedBy(target, _players.Get(attacker).AccountId))
        || (_players.Has(target) && _minds.Has(attacker) && OwnedBy(attacker, _players.Get(target).AccountId));

    private bool OwnedBy(Entity creature, ulong account)
    {
        Bonding.BondState bond = _world.Services.Bonding.GetBond(_minds.Get(creature).Handle);
        return bond.State == Bonding.OwnershipState.Pacted && bond.Owner == account;
    }

    // A ward takes effect once, on the ally the caster named if it is still there and in reach, otherwise on the caster.
    private void Ward(Entity caster, ref ActiveMove move, MoveDefinition definition, ulong now)
    {
        if (move.Hits > 0)
        {
            return;
        }

        Entity target = move.Target;
        bool ally = target != default && _ecs.IsAlive(target) && _combatants.Has(target) && _positions.Has(target)
            && Vector3.Distance(_positions.Get(target).Value, _positions.Get(caster).Value) <= definition.Shape.ReachCm / CentimetresPerMetre;
        target = ally ? target : caster;
        move.Hits = 1;
        if (definition.Effect is string effect)
        {
            StatusRules.Apply(_ecs, _world.Data, target, effect, now, definition.Heal);
        }

        if (target != caster && _players.Has(caster) && _ecs.Has<MindRef>(target))
        {
            PlayerInteractions.Remember(_world, target, "HealedBy", _players.Get(caster).AccountId, now);
        }
    }

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
    private const int SparedEventCapacity = 16;

    private readonly CreatureWorld _world;
    private readonly EcsWorld _ecs;
    private readonly ComponentStore<Combatant> _combatants;
    private readonly ComponentStore<Yielding> _yielding;
    private readonly ComponentStore<Downed> _downed;
    private readonly ComponentStore<PlayerAvatar> _players;
    private readonly ComponentStore<Blocking> _blocking;
    private readonly ComponentStore<Staggered> _staggered;
    private readonly ComponentStore<Dodging> _dodging;
    private readonly Entity[] _defeated;
    private readonly int _ticksPerSecond;

    /// <summary>Creates the system for a world.</summary>
    public VitalsSystem(CreatureWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!world.Events.IsRegistered<CreatureSpared>())
        {
            world.Events.Register<CreatureSpared>(SparedEventCapacity);
        }

        _world = world;
        _ecs = world.World;
        _combatants = _ecs.Store<Combatant>();
        _yielding = _ecs.Store<Yielding>();
        _downed = _ecs.Store<Downed>();
        _players = _ecs.Store<PlayerAvatar>();
        _blocking = _ecs.Store<Blocking>();
        _staggered = _ecs.Store<Staggered>();
        _dodging = _ecs.Store<Dodging>();
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

            Regenerate(ref c, rules, now, _blocking.Has(entities[i]));
        }

        Expire(rules, now);
        Spare(rules, now);
        for (int i = 0; i < defeated; i++)
        {
            Defeat(_defeated[i], now);
        }
    }

    private void Regenerate(ref Combatant c, CombatDefinition rules, ulong now, bool blocking)
    {
        ulong staminaFrom = c.LastSpendTick + (ulong)rules.StaminaRegenDelayTicks;
        if (c.Stamina < c.MaxStamina && now > staminaFrom && !blocking)
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

    // Ends staggers and dodges whose time is up (back to front: removal moves the last entry into the slot).
    private void Expire(CombatDefinition rules, ulong now)
    {
        ReadOnlySpan<Entity> staggered = _staggered.Entities;
        for (int i = staggered.Length - 1; i >= 0; i--)
        {
            if (now >= _staggered.Components[i].Until)
            {
                _ecs.Remove<Staggered>(staggered[i]);
            }
        }

        ReadOnlySpan<Entity> dodging = _dodging.Entities;
        for (int i = dodging.Length - 1; i >= 0; i--)
        {
            if (now - _dodging.Components[i].Since >= (ulong)rules.DodgeCooldownTicks)
            {
                _ecs.Remove<Dodging>(dodging[i]);
            }
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
                _world.Events.Publish(new CreatureSpared(creature, state.Account));
            }

            _ecs.Remove<Yielding>(creature);
        }
    }

    private void Defeat(Entity creature, ulong now)
    {
        ushort species = _ecs.GetComponent<SpeciesRef>(creature).Index;
        ulong account = _combatants.Get(creature).LastAttacker;
        int witnesses = account != 0 ? _world.WitnessKill(creature, species, account, now) : 0;
        Items.Fighters fighters = _ecs.Has<Items.Fighters>(creature) ? _ecs.GetComponent<Items.Fighters>(creature) : default;
        _world.Events.Publish(new CombatantDefeated(creature, species, account, witnesses, _ecs.GetComponent<Position>(creature).Value, fighters.With(account)));
        _world.DespawnCreature(creature);
    }
}
