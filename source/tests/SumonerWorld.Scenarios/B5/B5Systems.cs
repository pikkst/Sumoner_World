using System.Numerics;
using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecosystem;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Events;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.Spatial;
using SumonerWorld.Core.World;

namespace SumonerWorld.Scenarios.B5;

/// <summary>Stand-in players walk a fixed square route around the region (deterministic).</summary>
internal sealed class PlayerRouteSystem(IEcsWorld world, float regionSize, float speed) : ISimulationSystem
{
    private const float Margin = 100f;

    private readonly ComponentStore<PatrolRoute> _routes = world.Store<PatrolRoute>();
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
        ReadOnlySpan<Entity> players = _routes.Entities;
        Span<PatrolRoute> routes = _routes.Components;
        for (int i = 0; i < players.Length; i++)
        {
            ref MoveTarget target = ref _targets.Get(players[i]);
            if (target.Active == 0)
            {
                routes[i] = routes[i] with { Waypoint = routes[i].Waypoint + 1 };
                target = new MoveTarget(Waypoint(routes[i].Waypoint, regionSize), speed, 1);
            }
        }
    }
}

/// <summary>
/// Crowded creatures (more than 6 creatures sensed) remember the spot as dangerous (memory event <c>PlaceDanger</c>, subject =
/// the 16 m grid cell) and appraise it (opinion → emotions → emote, M1-07). A B5 stand-in for crowding stress.
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
        foreach (NeighboursSensed sensed in events.Read<NeighboursSensed>())
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
/// Every 10 s each stand-in player offers a Pact (feed daily + protect young) to the wild creatures that are watching it from close
/// by (M1-11). Most strangers refuse; the refusals are remembered.
/// </summary>
internal sealed class PactOfferSystem(IEcsWorld world, IBondingRules bonding, CreatureSubjectResolver resolver, SpeciesDefinition species, PactTerms terms)
    : ISimulationSystem
{
    private const int Interval = 300;
    private const int CloseEnough = 400;

    private readonly ComponentStore<FocusRef> _focus = world.Store<FocusRef>();
    private readonly ComponentStore<MindRef> _minds = world.Store<MindRef>();
    private readonly ComponentStore<PlayerAvatar> _players = world.Store<PlayerAvatar>();

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

/// <summary>
/// Every 30 ticks one creature dies and a new one spawns (ECS, spatial index, mind store and stable id churn). A death within
/// 30 m of a stand-in player counts as that player killing it, for the species' reputation (M1-12).
/// </summary>
internal sealed class ChurnSystem(B5Scenario scenario, CreatureWorld creatures, IReputationService reputation, ushort killedKin, ushort species)
    : ISimulationSystem
{
    private const int Interval = 30;
    private const float KillRange = 30f;

    private readonly ComponentStore<MindRef> _creatures = creatures.World.Store<MindRef>();
    private readonly ComponentStore<PlayerAvatar> _players = creatures.World.Store<PlayerAvatar>();
    private readonly ComponentStore<Position> _positions = creatures.World.Store<Position>();

    public SimulationStage Stage => SimulationStage.Ecosystem;

    public int KillsByPlayers { get; private set; }

    public void Execute(in SimulationTick tick)
    {
        if (tick.Index % Interval != 0 || _creatures.Count == 0)
        {
            return;
        }

        Entity victim = _creatures.Entities[0];
        Vector3 where = _positions.Get(victim).Value;
        ReadOnlySpan<Entity> players = _players.Entities;
        for (int i = 0; i < players.Length; i++)
        {
            if (Vector3.Distance(where, _positions.Get(players[i]).Value) <= KillRange)
            {
                reputation.Record(killedKin, new ReputationKey(species, creatures.RegionOf(where), _players.Get(players[i]).AccountId));
                KillsByPlayers++;
                break;
            }
        }

        creatures.DespawnCreature(victim);
        scenario.Spawn();
    }
}
