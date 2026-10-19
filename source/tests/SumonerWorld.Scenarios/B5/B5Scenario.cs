using System.Numerics;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Cognition.Decision;
using SumonerWorld.Core.Cognition.Lod;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Events;
using SumonerWorld.Core.Simulation;

namespace SumonerWorld.Scenarios.B5;

/// <summary>
/// Benchmark scenario B5 "Sim-Only" (M0-07 A1): 200 creatures and 2 stand-in players in the 512 m M1 test region, simulated by
/// Core without Godot. Creatures (species <c>mossback</c> from the shipped data) get an AI tier from their distance to the players
/// (M1-10), perceive neighbours and players at their tier's rate and focus on the most notable one, choose goals with the utility
/// AI (M1-08), carry them out with the shipped behaviour trees (M1-09), remember crowded spots as dangerous (M1-06), react with
/// emotions and emotes (M1-07), receive Pact offers from the players (M1-11), pass rumours to herd-mates and feed the species'
/// reputation, summed by a background job (M1-12), and are replaced by churn. Each new AI system is added here so the gates keep
/// covering it. Used by the CI gates (0 allocations per tick, identical runs) and by the benchmark tool (timings).
/// </summary>
public sealed class B5Scenario : IDisposable
{
    /// <summary>Creatures alive at any time.</summary>
    public const int CreatureCount = 200;

    /// <summary>Stand-in players walking a fixed route.</summary>
    public const int PlayerCount = 2;

    /// <summary>Warm-up: 30 s at 30 Hz (M0-07 A4).</summary>
    public const int WarmUpTicks = 900;

    /// <summary>Measured run: 5 min at 30 Hz (M0-07 A1).</summary>
    public const int MeasuredTicks = 9_000;

    private const ulong FirstAccountId = 9_000_001;
    private const int Herds = 10;
    private const int RegionCellSize = 256;
    private const int RegionsPerRow = 2;

    private readonly B5World _b5;
    private readonly StableIdAllocator _ids = new();
    private readonly SimulationScheduler _scheduler;
    private readonly B5Probes _probes;

    /// <summary>
    /// Builds the scenario from a seed; <paramref name="metrics"/> optionally receives stage timings; <paramref name="jobWorkers"/> is
    /// the number of background job threads (0 = run jobs inline). The result must not depend on it.
    /// </summary>
    public B5Scenario(ulong seed, IMetrics? metrics = null, int jobWorkers = 0)
    {
        _b5 = new B5World(seed, jobWorkers);
        _scheduler = new SimulationScheduler(_b5.Clock, _b5.Events, metrics);
        _probes = B5Pipeline.Register(_b5, _scheduler, this);

        for (int i = 0; i < PlayerCount; i++)
        {
            SpawnPlayer(i);
        }

        for (int i = 0; i < CreatureCount; i++)
        {
            Spawn();
        }
    }

    /// <summary>The scenario's event bus (its growth counter shows hidden allocations).</summary>
    public EventBus Events => _b5.Events;

    /// <summary>Creatures currently alive (players not counted).</summary>
    public int LiveCreatures => _b5.World.Store<MindRef>().Count;

    /// <summary>Minds currently allocated.</summary>
    public int LiveMinds => _b5.Minds.Count;

    /// <summary>How many creatures currently pursue each goal id (for the gate test).</summary>
    public IReadOnlyDictionary<ushort, int> GoalCounts => Count(_b5.World.Store<CurrentGoal>().Components, goal => goal.Goal);

    /// <summary>How many creatures are in each AI tier right now.</summary>
    public IReadOnlyDictionary<AiLodTier, int> TierCounts => Count(_b5.World.Store<AiTier>().Components, tier => tier.Tier);

    /// <summary>Creatures currently showing an emote.</summary>
    public int CreaturesShowingAnEmote => _b5.World.Store<CurrentEmote>().Components.ToArray().Count(emote => emote.Value != Emote.None);

    /// <summary>Times a creature chose a player as its focus since the start.</summary>
    public int PlayerFocusEvents => _probes.Perception.PlayerFocusEvents;

    /// <summary>Rumours passed between herd-mates since the start.</summary>
    public int RumoursShared => _probes.Perception.RumoursShared;

    /// <summary>Creature deaths attributed to a nearby player (reputation events) since the start.</summary>
    public int KillsByPlayers => _probes.Churn.KillsByPlayers;

    /// <summary>Reputation aggregation jobs applied since the start.</summary>
    public int ReputationAggregations => _b5.Reputation.Aggregations;

    /// <summary>Pact offers made by the players since the start.</summary>
    public int PactOffers => _probes.Pacts.Offers;

    /// <summary>Pact offers accepted since the start.</summary>
    public int PactsSealed => _probes.Pacts.Accepted;

    /// <summary>Interact intents carried out (eat, rest, socialise...) since the start.</summary>
    public int Interactions => _probes.Intents.Interactions;

    /// <summary>Memory records currently stored by all creatures.</summary>
    public int StoredMemories => _b5.Minds.TotalRecords;

    /// <summary>Simulates one tick.</summary>
    public void RunTick() => _scheduler.RunTick();

    /// <summary>Hash of all deterministic state: entities, components, minds, bonds and reputation (TDD §2.3).</summary>
    public ulong ComputeStateHash()
    {
        EcsWorld world = _b5.World;
        var hash = default(StateHash);
        hash.Add(_b5.Clock.Current.Index);
        hash.Add<Entity>(world.Store<Position>().Entities);
        hash.Add<StableId>(world.Store<StableId>().Components);
        hash.Add<Position>(world.Store<Position>().Components);
        hash.Add<Facing>(world.Store<Facing>().Components);
        hash.Add<MoveTarget>(world.Store<MoveTarget>().Components);
        hash.Add<AiTier>(world.Store<AiTier>().Components);
        hash.Add<FocusRef>(world.Store<FocusRef>().Components);
        hash.Add<CurrentEmote>(world.Store<CurrentEmote>().Components);
        hash.Add<CurrentGoal>(world.Store<CurrentGoal>().Components);
        hash.Add<GroupRef>(world.Store<GroupRef>().Components);
        _b5.Minds.AddTo(ref hash);
        _probes.Bonding.AddTo(ref hash);
        _b5.Reputation.AddTo(ref hash);
        return hash.Value;
    }

    /// <summary>Stops the background job threads.</summary>
    public void Dispose() => _b5.Dispose();

    internal static int RegionOf(Vector3 position) =>
        ((int)(position.Z / RegionCellSize) * RegionsPerRow) + (int)(position.X / RegionCellSize);

    internal void Spawn()
    {
        EcsWorld world = _b5.World;
        Entity creature = world.Create();
        var position = new Vector3((float)(_b5.Random.NextDouble() * B5World.RegionSize), 0, (float)(_b5.Random.NextDouble() * B5World.RegionSize));
        world.Add(creature, _ids.Allocate());
        AddBody(creature, position);
        world.Add(creature, new CurrentEmote(Emote.None));
        world.Add(creature, new CurrentGoal(Decision.Idle));
        world.Add(creature, new AiTier(AiLodTier.Far));
        world.Add(creature, default(FocusRef));
        MindHandle mind = _b5.Minds.Allocate(Personality.Roll(_b5.Species, _b5.Random), _b5.Species.Needs);
        world.Add(creature, new MindRef(mind));
        world.Add(creature, new GroupRef(_b5.Random.NextInt(0, Herds)));
        _b5.Reputation.SeedOpinions(_b5.Minds, mind, species: 0, RegionOf(position));

        // Creatures enter the world with varied needs, so goals differ from the start.
        ulong now = _b5.Clock.Current.Index;
        _b5.Minds.SetNeed(mind, Need.Hunger, _b5.Random.NextInt(0, ICreatureMindStore.MaxNeed + 1), now);
        _b5.Minds.SetNeed(mind, Need.Rest, _b5.Random.NextInt(0, ICreatureMindStore.MaxNeed + 1), now);
        _b5.Minds.SetNeed(mind, Need.Social, _b5.Random.NextInt(0, ICreatureMindStore.MaxNeed + 1), now);
    }

    internal void Despawn(Entity creature)
    {
        _b5.Grid.Remove(creature);
        _b5.Minds.Release(_b5.World.GetComponent<MindRef>(creature).Handle);
        _b5.World.Destroy(creature);
    }

    private static Dictionary<TKey, int> Count<T, TKey>(Span<T> values, Func<T, TKey> key)
        where TKey : notnull
    {
        var counts = new Dictionary<TKey, int>();
        foreach (T value in values)
        {
            counts[key(value)] = counts.GetValueOrDefault(key(value)) + 1;
        }

        return counts;
    }

    private void SpawnPlayer(int index)
    {
        Entity player = _b5.World.Create();
        Vector3 start = PlayerRouteSystem.Waypoint(index * 2, B5World.RegionSize);
        _b5.World.Add(player, new PlayerTag(FirstAccountId + (ulong)index, index * 2));
        AddBody(player, start);
    }

    private void AddBody(Entity entity, Vector3 position)
    {
        _b5.World.Add(entity, new Position(position));
        _b5.World.Add(entity, new Facing(Vector3.UnitZ));
        _b5.World.Add(entity, new MoveTarget(position, 0, 0));
        _b5.Grid.Insert(entity, position);
    }
}
