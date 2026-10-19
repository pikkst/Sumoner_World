using System.Numerics;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Cognition.Lod;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Events;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.World;

namespace SumonerWorld.Scenarios.B5;

/// <summary>
/// Benchmark scenario B5 "Sim-Only" (M0-07 A1): 200 creatures and 2 stand-in players in the 512 m M1 test region, simulated by the
/// production <see cref="CreatureWorld"/> without Godot: AI tiers, perception and focus, utility AI, behaviour trees, memory,
/// emotions and emotes, rumours and reputation. B5 adds stand-ins for what has no real source yet: players walking a route,
/// crowding stress, Pact offers and churn (deaths near players feed reputation). Used by the CI gates (0 allocations per tick,
/// identical runs, worker-thread independence) and by the benchmark tool (timings).
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

    private const float RegionSize = 512f;
    private const float PlayerSpeed = 0.15f;
    private const string SpeciesId = "mossback";
    private const ulong FirstAccountId = 9_000_001;
    private const int Herds = 10;

    private readonly CreatureWorld _world;
    private readonly SpeciesDefinition _species;
    private readonly ushort _speciesIndex;
    private readonly SimulationScheduler _scheduler;
    private readonly B5Probes _probes;

    /// <summary>
    /// Builds the scenario from a seed; <paramref name="metrics"/> optionally receives stage timings; <paramref name="jobWorkers"/> is
    /// the number of background job threads (0 = run jobs inline). The result must not depend on it.
    /// </summary>
    public B5Scenario(ulong seed, IMetrics? metrics = null, int jobWorkers = 0)
    {
        _world = new CreatureWorld(
            ShippedData.Current,
            new CreatureWorldSettings { BoundsMax = new Vector3(RegionSize, 0, RegionSize), JobWorkers = jobWorkers },
            seed);
        _world.World.Register<PatrolRoute>();
        _speciesIndex = _world.SpeciesIndex(SpeciesId);
        _species = _world.Data.Species[_speciesIndex];
        _scheduler = new SimulationScheduler(_world.Clock, _world.Events, metrics);
        _probes = B5Pipeline.Register(_world, _scheduler, this, _speciesIndex, RegionSize, PlayerSpeed);

        for (int i = 0; i < PlayerCount; i++)
        {
            Entity player = _world.SpawnPlayerAvatar(FirstAccountId + (ulong)i, PlayerRouteSystem.Waypoint(i * 2, RegionSize));
            _world.World.Add(player, new PatrolRoute(i * 2));
        }

        for (int i = 0; i < CreatureCount; i++)
        {
            Spawn();
        }
    }

    /// <summary>The scenario's event bus (its growth counter shows hidden allocations).</summary>
    public EventBus Events => _world.Events;

    /// <summary>Creatures currently alive (players not counted).</summary>
    public int LiveCreatures => _world.World.Store<MindRef>().Count;

    /// <summary>Minds currently allocated.</summary>
    public int LiveMinds => _world.Minds.Count;

    /// <summary>How many creatures currently pursue each goal id (for the gate test).</summary>
    public IReadOnlyDictionary<ushort, int> GoalCounts => Count(_world.World.Store<CurrentGoal>().Components, goal => goal.Goal);

    /// <summary>How many creatures are in each AI tier right now.</summary>
    public IReadOnlyDictionary<AiLodTier, int> TierCounts => Count(_world.World.Store<AiTier>().Components, tier => tier.Tier);

    /// <summary>Creatures currently showing an emote.</summary>
    public int CreaturesShowingAnEmote => _world.World.Store<CurrentEmote>().Components.ToArray().Count(emote => emote.Value != Emote.None);

    /// <summary>Times a creature chose a player as its focus since the start.</summary>
    public int PlayerFocusEvents => _probes.Creatures.Perception.PlayerFocusEvents;

    /// <summary>Rumours passed between herd-mates since the start.</summary>
    public int RumoursShared => _probes.Creatures.Perception.RumoursShared;

    /// <summary>Creature deaths attributed to a nearby player (reputation events) since the start.</summary>
    public int KillsByPlayers => _probes.Churn.KillsByPlayers;

    /// <summary>Reputation aggregation jobs applied since the start.</summary>
    public int ReputationAggregations => _world.Reputation.Aggregations;

    /// <summary>Pact offers made by the players since the start.</summary>
    public int PactOffers => _probes.Pacts.Offers;

    /// <summary>Pact offers accepted since the start.</summary>
    public int PactsSealed => _probes.Pacts.Accepted;

    /// <summary>Interact intents carried out (eat, rest, socialise...) since the start.</summary>
    public int Interactions => _probes.Creatures.Intents.Interactions;

    /// <summary>Memory records currently stored by all creatures.</summary>
    public int StoredMemories => _world.Minds.TotalRecords;

    /// <summary>Simulates one tick.</summary>
    public void RunTick() => _scheduler.RunTick();

    /// <summary>Hash of all deterministic state (TDD §2.3).</summary>
    public ulong ComputeStateHash()
    {
        var hash = default(StateHash);
        _world.AddTo(ref hash, _world.Services.Bonding);
        return hash.Value;
    }

    /// <summary>Stops the background job threads.</summary>
    public void Dispose() => _world.Dispose();

    internal void Spawn()
    {
        // Random draws in a fixed order: position, personality, herd, needs (R11).
        var position = new Vector3((float)(_world.Random.NextDouble() * RegionSize), 0, (float)(_world.Random.NextDouble() * RegionSize));
        Personality personality = Personality.Roll(_species, _world.Random);
        int herd = _world.Random.NextInt(0, Herds);
        Entity creature = _world.SpawnCreature(_speciesIndex, position, personality, herd);

        // Creatures enter the world with varied needs, so goals differ from the start.
        MindHandle mind = _world.World.GetComponent<MindRef>(creature).Handle;
        ulong now = _world.Clock.Current.Index;
        _world.Minds.SetNeed(mind, Need.Hunger, _world.Random.NextInt(0, ICreatureMindStore.MaxNeed + 1), now);
        _world.Minds.SetNeed(mind, Need.Rest, _world.Random.NextInt(0, ICreatureMindStore.MaxNeed + 1), now);
        _world.Minds.SetNeed(mind, Need.Social, _world.Random.NextInt(0, ICreatureMindStore.MaxNeed + 1), now);
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
}
