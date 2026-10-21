using System.Numerics;
using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.World;

namespace SumonerWorld.Core.Progression;

/// <summary>Published when a creature reaches a new level (feedback, telemetry).</summary>
/// <param name="Creature">The creature.</param>
/// <param name="Level">Its new level.</param>
public readonly record struct CreatureLevelledUp(Entity Creature, int Level);

/// <summary>Published when a companion grows into a new life stage.</summary>
/// <param name="Creature">The creature.</param>
/// <param name="Stage">Its new stage.</param>
public readonly record struct CreatureGrewUp(Entity Creature, LifeStage Stage);

/// <summary>
/// Companion growth (companions spec §3–4, M2-09). Summoned companions earn experience by role: <b>combat</b> from damage they deal
/// and take (one point per <see cref="GrowthDefinition.DamagePerCombatXp"/>) and from a defeated foe, <b>travel</b> from the
/// distance they walk. The level follows the total, up to the tier's cap; stats follow the level. When a companion may grow into
/// the next life stage (level share of its cap and bond level), it does so while its summoner rests (stands still, no fight, for
/// <see cref="GrowthDefinition.RestTicks"/>): it shows Affection and remembers the moment as a core memory with them (<c>GrewUp</c>).
/// </summary>
public sealed class GrowthSystem : ISimulationSystem
{
    private const int CentimetresPerMetre = 100;
    private const int EventCapacity = 16;
    private const float MaxStepMetres = 2f; // more in one tick is a summon or a catch-up, not a walk

    private readonly CreatureWorld _world;
    private readonly ComponentStore<CreatureGrowth> _growth;
    private readonly ComponentStore<CompanionState> _companions;
    private readonly ComponentStore<MindRef> _minds;
    private readonly ComponentStore<Position> _positions;
    private readonly ComponentStore<Combatant> _combatants;
    private readonly ComponentStore<SpeciesRef> _species;
    private readonly ComponentStore<CurrentEmote> _emotes;
    private readonly ComponentStore<PlayerAvatar> _players;
    private readonly ComponentStore<PlayerProgress> _progress;
    private readonly Vector3[] _last;
    private readonly ushort _grewUp;

    /// <summary>Creates the system for a world.</summary>
    public GrowthSystem(CreatureWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
        EcsWorld ecs = world.World;
        _growth = ecs.Store<CreatureGrowth>();
        _companions = ecs.Store<CompanionState>();
        _minds = ecs.Store<MindRef>();
        _positions = ecs.Store<Position>();
        _combatants = ecs.Store<Combatant>();
        _species = ecs.Store<SpeciesRef>();
        _emotes = ecs.Store<CurrentEmote>();
        _players = ecs.Store<PlayerAvatar>();
        _progress = ecs.Store<PlayerProgress>();
        _last = new Vector3[ecs.Capacity];
        _grewUp = world.Rules.FindByKey("GrewUp") ?? throw new InvalidOperationException("The data has no 'GrewUp' memory event type.");
        if (!world.Events.IsRegistered<CreatureLevelledUp>())
        {
            world.Events.Register<CreatureLevelledUp>(EventCapacity);
            world.Events.Register<CreatureGrewUp>(EventCapacity);
        }
    }

    /// <inheritdoc />
    public SimulationStage Stage => SimulationStage.StatusEffects;

    /// <inheritdoc />
    public void Execute(in SimulationTick tick)
    {
        GrowthDefinition rules = _world.Growth.Definition;
        foreach (CombatHit hit in _world.Events.Read<CombatHit>())
        {
            int xp = hit.Damage / rules.DamagePerCombatXp;
            Earn(hit.Attacker, XpRole.Combat, xp);
            Earn(hit.Target, XpRole.Combat, xp);
        }

        foreach (CombatantDefeated defeated in _world.Events.Read<CombatantDefeated>())
        {
            EarnPlayer(defeated.ByAccount, rules.PlayerDefeatXp);
            ReadOnlySpan<Entity> companions = _companions.Entities;
            for (int i = 0; i < companions.Length; i++)
            {
                if (_companions.Components[i].Foe == defeated.Target)
                {
                    Earn(companions[i], XpRole.Combat, rules.DefeatXp);
                }
            }
        }

        if (_world.Events.IsRegistered<CreatureSpared>())
        {
            foreach (CreatureSpared spared in _world.Events.Read<CreatureSpared>())
            {
                EarnPlayer(spared.Account, rules.PlayerSpareXp);
            }
        }

        Travel(rules);
        GrowUp(rules, tick.Index);
    }

    // The player who defeated or spared a creature gains experience (GDD §5).
    private void EarnPlayer(ulong account, int xp)
    {
        if (account == 0)
        {
            return;
        }

        ReadOnlySpan<Entity> players = _players.Entities;
        for (int i = 0; i < players.Length; i++)
        {
            if (_players.Components[i].AccountId == account && _progress.Has(players[i]))
            {
                PlayerGrowth.Earn(_world.Growth, ref _progress.Get(players[i]), ref _combatants.Get(players[i]), _world.Data.Combat.Player, xp);
            }
        }
    }

    // Walking at the summoner's side, measured on the ground plane.
    private void Travel(GrowthDefinition rules)
    {
        ReadOnlySpan<Entity> companions = _companions.Entities;
        for (int i = 0; i < companions.Length; i++)
        {
            Entity companion = companions[i];
            Vector3 now = _positions.Get(companion).Value;
            Vector3 before = _last[companion.Index];
            _last[companion.Index] = now;
            float step = new Vector2(now.X - before.X, now.Z - before.Z).Length();
            if (before == Vector3.Zero || step > MaxStepMetres || !_growth.Has(companion))
            {
                continue;
            }

            ref CreatureGrowth growth = ref _growth.Get(companion);
            growth.TravelCm += (int)(step * CentimetresPerMetre);
            int perPoint = rules.MetresPerTravelXp * CentimetresPerMetre;
            if (growth.TravelCm >= perPoint)
            {
                int points = growth.TravelCm / perPoint;
                growth.TravelCm -= points * perPoint;
                Earn(companion, XpRole.Travel, points);
            }
        }
    }

    private void GrowUp(GrowthDefinition rules, ulong now)
    {
        ReadOnlySpan<Entity> companions = _companions.Entities;
        for (int i = 0; i < companions.Length; i++)
        {
            Entity companion = companions[i];
            CompanionState state = _companions.Components[i];
            if (!_growth.Has(companion) || state.Mode == (int)CompanionMode.Fight || now - state.OwnerStillSince < (ulong)rules.RestTicks)
            {
                continue;
            }

            ref CreatureGrowth growth = ref _growth.Get(companion);
            MindHandle mind = _minds.Get(companion).Handle;
            BondState bond = _world.Services.Bonding.GetBond(mind);
            LifeStage next = _world.Growth.StageAllowed(growth, bond.BondLevel);
            if (next == (LifeStage)growth.Stage)
            {
                continue;
            }

            growth.Stage = (int)next;
            Restat(companion, growth);
            _world.Minds.Remember(mind, _grewUp, new MemorySubject(SubjectKind.Player, bond.Owner), now);
            _emotes.Get(companion) = new CurrentEmote(Emote.Affection);
            _world.Services.Bonding.RecalculateBondLevel(mind, now);
            _world.Events.Publish(new CreatureGrewUp(companion, next));
        }
    }

    // Only summoned companions grow (wild creatures keep the level they have).
    private void Earn(Entity creature, XpRole role, int xp)
    {
        if (xp <= 0 || !_companions.Has(creature) || !_growth.Has(creature))
        {
            return;
        }

        ref CreatureGrowth growth = ref _growth.Get(creature);
        switch (role)
        {
            case XpRole.Combat:
                growth.CombatXp += xp;
                break;
            case XpRole.Work:
                growth.WorkXp += xp;
                break;
            default:
                growth.TravelXp += xp;
                break;
        }

        int level = _world.Growth.LevelFor(growth.TotalXp, (Rank)growth.Tier);
        if (level > growth.Level)
        {
            growth.Level = level;
            Restat(creature, growth);
            _world.Events.Publish(new CreatureLevelledUp(creature, level));
        }
    }

    private void Restat(Entity creature, in CreatureGrowth growth) =>
        _world.Growth.Apply(ref _combatants.Get(creature), _world.Data.Species[_species.Get(creature).Index].Combat, growth);
}
