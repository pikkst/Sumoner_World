using System.Runtime.CompilerServices;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Randomness;

namespace SumonerWorld.Tests.Core.Cognition;

[Collection(AllocationMeasurements.Name)]
public class CreatureMindStoreTests
{
    private const int TicksPerDay = 86_400;
    private const ushort FedBy = 1;
    private const ushort SavedLifeOf = 3;
    private const ushort AttackedBy = 5;
    private const ushort BondedDied = 14;
    private const ushort PlaceDanger = 16;

    private static readonly MemorySubject Alice = new(SubjectKind.Player, 1001);
    private static readonly MemorySubject Bob = new(SubjectKind.Player, 1002);
    private static readonly Personality Birth = new(Boldness: -30, Loyalty: 40, Curiosity: 10, Temper: -40);
    private static readonly NeedRates Needs = new() { Hunger = 60, Rest = 40, Safety = 50, Social = 70, Territory = 10 };

    [Fact]
    public void A_memory_record_is_32_bytes()
    {
        Assert.Equal(32, Unsafe.SizeOf<MemoryRecord>());
    }

    [Fact]
    public void A_new_memory_starts_at_its_base_salience_and_fades()
    {
        var (store, mind) = CreateStoreWithMind();

        RememberResult result = store.Remember(mind, FedBy, Alice, now: 0);
        RecalledMemory now = RecallAll(store, mind, 0)[0];
        RecalledMemory later = RecallAll(store, mind, 2 * TicksPerDay)[0];

        Assert.Equal(MemoryOutcome.Created, result.Outcome);
        Assert.Equal(150, now.EffectiveSalience);
        Assert.Equal(75, later.EffectiveSalience);
    }

    [Fact]
    public void Repeated_episodes_reinforce_one_memory_up_to_the_cap()
    {
        var (store, mind) = CreateStoreWithMind();

        for (int day = 0; day < 30; day++)
        {
            store.Remember(mind, FedBy, Alice, now: (ulong)day * TicksPerDay);
        }

        RecalledMemory fed = Assert.Single(RecallAll(store, mind, 29UL * TicksPerDay));
        Assert.Equal(30, fed.Record.Count);
        Assert.True(fed.EffectiveSalience > 150, "Daily feeding must build a stronger memory than one meal.");
        Assert.True(fed.EffectiveSalience <= ICreatureMindStore.MaxSalience);
    }

    [Fact]
    public void A_forgotten_memory_is_not_recalled_and_restarts_when_it_happens_again()
    {
        var (store, mind) = CreateStoreWithMind();
        store.Remember(mind, FedBy, Alice, now: 0);
        ulong muchLater = 20UL * TicksPerDay;

        Assert.Empty(RecallAll(store, mind, muchLater));

        RememberResult again = store.Remember(mind, FedBy, Alice, muchLater);

        Assert.Equal(MemoryOutcome.Created, again.Outcome);
        Assert.Equal(1, RecallAll(store, mind, muchLater)[0].Record.Count);
        Assert.Single(store.GetEpisodicRecords(mind).ToArray());
    }

    [Fact]
    public void A_full_episodic_store_drops_the_weakest_memory()
    {
        var (store, mind) = CreateStoreWithMind();
        store.Remember(mind, PlaceDanger, Place(0), now: 0);
        for (int i = 1; i < ICreatureMindStore.EpisodicCapacity; i++)
        {
            store.Remember(mind, AttackedBy, Player(i), now: 100);
        }

        RememberResult result = store.Remember(mind, AttackedBy, Player(999), now: 200);

        Assert.True(result.EvictedAnother);
        Assert.Equal(ICreatureMindStore.EpisodicCapacity, store.GetEpisodicRecords(mind).Length);
        Assert.DoesNotContain(store.GetEpisodicRecords(mind).ToArray(), record => record.EventType == PlaceDanger);
    }

    [Fact]
    public void Core_memories_are_pinned_never_fade_below_the_floor_and_shift_personality_once()
    {
        var (store, mind) = CreateStoreWithMind();

        RememberResult formed = store.Remember(mind, SavedLifeOf, Alice, now: 0);
        RememberResult again = store.Remember(mind, SavedLifeOf, Alice, now: 10);

        Assert.Equal(MemoryOutcome.CoreFormed, formed.Outcome);
        Assert.Equal(PersonalityTrait.Loyalty, formed.DriftedTrait);
        Assert.Equal(MemoryOutcome.Reinforced, again.Outcome);
        Assert.Equal(Birth.Loyalty + 3, store.GetPersonality(mind).Loyalty);
        Assert.Equal(Birth, store.GetBirthPersonality(mind));

        store.Remember(mind, AttackedBy, Bob, now: 0);
        Assert.True(store.GetCoreRecords(mind)[0].IsPinned);
    }

    [Fact]
    public void Pinned_memories_with_a_half_life_stop_at_the_floor()
    {
        var rules = new MemoryRules([Definition(50, "Rescued", isCore: true, halfLifeDays: 1, salience: 800)], TicksPerDay);
        var store = new CreatureMindStore(rules, capacity: 1);
        MindHandle mind = store.Allocate(Birth, Needs);
        store.Remember(mind, 50, Alice, now: 0);

        Assert.Equal(ICreatureMindStore.PinnedFloor, RecallAll(store, mind, 100UL * TicksPerDay)[0].EffectiveSalience);
    }

    [Fact]
    public void A_ninth_core_memory_replaces_the_weakest_only_if_stronger_and_the_old_one_becomes_episodic()
    {
        var (store, mind) = CreateStoreWithMind();
        for (int i = 0; i < ICreatureMindStore.CoreCapacity; i++)
        {
            store.Remember(mind, BondedDied, Creature(i), now: (ulong)i);
        }

        // SavedLifeOf (900) is not stronger than BondedDied (900, never decays): it is stored as an ordinary memory.
        RememberResult weak = store.Remember(mind, SavedLifeOf, Alice, now: 100);
        Assert.Equal(MemoryOutcome.Created, weak.Outcome);
        Assert.DoesNotContain(store.GetCoreRecords(mind).ToArray(), record => record.EventType == SavedLifeOf);

        var rules = new MemoryRules([Definition(60, "Small", isCore: true, halfLifeDays: null, salience: 400, creatures: true), Definition(61, "Big", isCore: true, halfLifeDays: null, salience: 950, creatures: true)], TicksPerDay);
        var store2 = new CreatureMindStore(rules, capacity: 1);
        MindHandle mind2 = store2.Allocate(Birth, Needs);
        for (int i = 0; i < ICreatureMindStore.CoreCapacity; i++)
        {
            store2.Remember(mind2, 60, Creature(i), now: 0);
        }

        RememberResult strong = store2.Remember(mind2, 61, Creature(99), now: 1);

        Assert.Equal(MemoryOutcome.CoreFormed, strong.Outcome);
        Assert.Contains(store2.GetCoreRecords(mind2).ToArray(), record => record.EventType == 61);
        MemoryRecord displaced = Assert.Single(store2.GetEpisodicRecords(mind2).ToArray());
        Assert.False(displaced.IsPinned);
        Assert.Equal(60, displaced.EventType);
    }

    [Fact]
    public void Personality_drift_is_capped_at_25_from_birth()
    {
        // 8 core slots x 5 points = 40 points of pushes; the cap keeps the creature within 25 of its birth value.
        var rules = new MemoryRules(
            [Definition(70, "Shaped", isCore: true, halfLifeDays: null, salience: 900, drift: new TraitDrift { Trait = PersonalityTrait.Temper, Amount = 5 })],
            TicksPerDay);
        var store = new CreatureMindStore(rules, capacity: 1);
        MindHandle mind = store.Allocate(Birth, Needs);

        for (int i = 0; i < ICreatureMindStore.CoreCapacity; i++)
        {
            store.Remember(mind, 70, Player(i), now: (ulong)i);
        }

        Assert.Equal(Birth.Temper + Personality.MaxDrift, store.GetPersonality(mind).Temper);
    }

    [Fact]
    public void Recall_is_strongest_first_and_can_filter_by_subject()
    {
        var (store, mind) = CreateStoreWithMind();
        store.Remember(mind, FedBy, Alice, now: 0);
        store.Remember(mind, AttackedBy, Alice, now: 0);
        store.Remember(mind, AttackedBy, Bob, now: 0);

        RecalledMemory[] all = RecallAll(store, mind, 0);
        var aboutAlice = new RecalledMemory[8];
        int count = store.RecallAbout(mind, Alice, 0, aboutAlice);
        var top = new RecalledMemory[1];
        store.Recall(mind, 0, top);

        Assert.Equal([400, 400, 150], all.Select(memory => memory.EffectiveSalience));
        Assert.Equal(2, count);
        Assert.All(aboutAlice.Take(count), memory => Assert.Equal(Alice, memory.Record.Subject));
        Assert.Equal(all[0], top[0]);
    }

    [Fact]
    public void Invalid_use_fails_loudly()
    {
        var (store, mind) = CreateStoreWithMind();

        Assert.Throws<ArgumentException>(() => store.Remember(mind, FedBy, Place(1), now: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.Remember(mind, 999, Alice, now: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.Remember(mind, FedBy, Alice, now: (ulong)uint.MaxValue + 1));

        store.Remember(mind, FedBy, Alice, now: 100);
        Assert.Throws<ArgumentOutOfRangeException>(() => store.Remember(mind, FedBy, Alice, now: 50));

        store.Release(mind);
        Assert.Throws<InvalidOperationException>(() => store.Remember(mind, FedBy, Alice, now: 200));
        Assert.False(store.IsValid(mind));
        Assert.Empty(store.GetCoreRecords(store.Allocate(Birth, Needs)).ToArray());
    }

    [Fact]
    public void Random_episodes_keep_every_invariant()
    {
        // Property test: 20,000 seeded random episodes over 60 in-game days for 4 creatures.
        ushort[] events = [FedBy, 2, SavedLifeOf, 4, AttackedBy, 6, 7, 8, 9, 10, 11, 12, 13, BondedDied, 15, 17];
        var random = new SeededRandom(0xBEEF);
        var store = new CreatureMindStore(ShippedRules(), capacity: 4);
        MindHandle[] minds = [.. Enumerable.Range(0, 4).Select(_ => store.Allocate(Birth, Needs))];
        ulong now = 0;

        for (int step = 0; step < 20_000; step++)
        {
            now += (ulong)random.NextInt(0, 300);
            MindHandle mind = minds[random.NextInt(0, minds.Length)];
            ushort eventType = events[random.NextInt(0, events.Length)];
            MemorySubject subject = eventType == BondedDied ? Creature(random.NextInt(0, 40)) : Player(random.NextInt(0, 40));
            store.Remember(mind, eventType, subject, now);

            ReadOnlySpan<MemoryRecord> core = store.GetCoreRecords(mind);
            ReadOnlySpan<MemoryRecord> episodic = store.GetEpisodicRecords(mind);
            Assert.True(core.Length <= ICreatureMindStore.CoreCapacity);
            Assert.True(episodic.Length <= ICreatureMindStore.EpisodicCapacity);
            foreach (MemoryRecord record in core)
            {
                Assert.True(record.IsPinned);
            }

            var keys = new HashSet<(ushort, MemorySubject)>();
            foreach (MemoryRecord record in (MemoryRecord[])[.. core, .. episodic])
            {
                Assert.InRange(record.Salience, 1, ICreatureMindStore.MaxSalience);
                Assert.True(keys.Add((record.EventType, record.Subject)), "Each event + subject is stored at most once.");
            }

            Personality current = store.GetPersonality(mind);
            Assert.InRange(current.Loyalty, Birth.Loyalty - Personality.MaxDrift, Birth.Loyalty + Personality.MaxDrift);
        }
    }

    [Fact]
    public void Steady_state_remembering_and_recalling_allocate_nothing()
    {
        var store = new CreatureMindStore(ShippedRules(), capacity: 200);
        MindHandle[] minds = [.. Enumerable.Range(0, 200).Select(_ => store.Allocate(Birth, Needs))];
        var results = new RecalledMemory[16];
        for (int i = 0; i < 200; i++)
        {
            store.Remember(minds[i], AttackedBy, Player(i), now: 0);
        }

        // Warm-up: one pass so one-time static initialisation is not counted.
        for (int i = 0; i < minds.Length; i++)
        {
            store.Remember(minds[i], FedBy, Player(0), now: 1);
            store.Recall(minds[i], 1, results);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (ulong tick = 2; tick <= 300; tick++)
        {
            for (int i = 0; i < minds.Length; i++)
            {
                store.Remember(minds[i], FedBy, Player((int)(tick % 60)), tick);
                store.Recall(minds[i], tick, results);
            }
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void Rolled_personalities_stay_inside_the_species_range_and_repeat_for_the_same_seed()
    {
        var species = new SpeciesDefinition
        {
            Id = "test",
            NameKey = "k",
            Role = CreatureRole.Grazer,
            BodyType = BodyType.Quadruped,
            Group = GroupType.Herd,
            Boldness = new TraitRange { Mean = -90, Spread = 30 },
            Loyalty = new TraitRange { Mean = 0, Spread = 0 },
            Curiosity = new TraitRange { Mean = 50, Spread = 10 },
            Temper = new TraitRange { Mean = 0, Spread = 100 },
            Needs = new NeedRates { Hunger = 1, Rest = 1, Safety = 1, Social = 1, Territory = 1 },
            PactThreshold = 1,
            Perception = new PerceptionDefinition { SightRange = 50, SightHalfAngle = 120, HearingRange = 30 },
            Goals = ["Eat"],
        };

        for (ulong seed = 0; seed < 200; seed++)
        {
            Personality p = Personality.Roll(species, new SeededRandom(seed));
            Assert.InRange(p.Boldness, Personality.MinTrait, -60);
            Assert.Equal(0, p.Loyalty);
            Assert.InRange(p.Curiosity, 40, 60);
            Assert.Equal(p, Personality.Roll(species, new SeededRandom(seed)));
        }
    }

    private static (CreatureMindStore Store, MindHandle Mind) CreateStoreWithMind()
    {
        var store = new CreatureMindStore(ShippedRules(), capacity: 4);
        return (store, store.Allocate(Birth, Needs));
    }

    private static MemoryRules ShippedRules() =>
        new(
        [
            Definition(FedBy, "FedBy", isCore: false, halfLifeDays: 2, salience: 150, valence: 20),
            Definition(2, "HealedBy", isCore: false, halfLifeDays: 7, salience: 300),
            Definition(SavedLifeOf, "SavedLifeOf", isCore: true, halfLifeDays: null, salience: 900, drift: new TraitDrift { Trait = PersonalityTrait.Loyalty, Amount = 3 }, creatures: true),
            Definition(4, "ProtectedYoung", isCore: true, halfLifeDays: 30, salience: 700),
            Definition(AttackedBy, "AttackedBy", isCore: false, halfLifeDays: 3, salience: 400, valence: -40, creatures: true),
            Definition(6, "DefeatedButSpared", isCore: false, halfLifeDays: 14, salience: 500),
            Definition(7, "KilledKin", isCore: true, halfLifeDays: 60, salience: 900, drift: new TraitDrift { Trait = PersonalityTrait.Temper, Amount = 3 }),
            Definition(8, "HuntedMyKind", isCore: false, halfLifeDays: 10, salience: 400),
            Definition(9, "PactOffered", isCore: false, halfLifeDays: 2, salience: 100),
            Definition(10, "PactTermKept", isCore: false, halfLifeDays: 7, salience: 150),
            Definition(11, "PactBroken", isCore: true, halfLifeDays: 90, salience: 800, drift: new TraitDrift { Trait = PersonalityTrait.Loyalty, Amount = -3 }),
            Definition(12, "CommandedIntoDanger", isCore: false, halfLifeDays: 5, salience: 350),
            Definition(13, "FoughtBeside", isCore: false, halfLifeDays: 7, salience: 250),
            Definition(BondedDied, "BondedDied", isCore: true, halfLifeDays: null, salience: 900, onlyCreatures: true),
            Definition(15, "OwnerDied", isCore: false, halfLifeDays: 3, salience: 500),
            Definition(PlaceDanger, "PlaceDanger", isCore: false, halfLifeDays: 14, salience: 300, onlyPlaces: true),
            Definition(17, "Mistreated", isCore: false, halfLifeDays: 14, salience: 450),
        ],
            TicksPerDay);

    private static MemoryEventTypeDefinition Definition(
        ushort id, string key, bool isCore, int? halfLifeDays, int salience, int valence = 10, TraitDrift? drift = null,
        bool creatures = false, bool onlyCreatures = false, bool onlyPlaces = false) => new()
        {
            Id = id,
            Key = key,
            Subjects = onlyPlaces ? [SubjectKind.Place] : onlyCreatures ? [SubjectKind.Creature] : creatures ? [SubjectKind.Player, SubjectKind.Creature] : [SubjectKind.Player],
            Valence = valence,
            BaseSalience = salience,
            HalfLifeDays = halfLifeDays,
            IsCore = isCore,
            TraitDrift = drift,
        };

    private static RecalledMemory[] RecallAll(CreatureMindStore store, MindHandle mind, ulong now)
    {
        var buffer = new RecalledMemory[ICreatureMindStore.CoreCapacity + ICreatureMindStore.EpisodicCapacity];
        int count = store.Recall(mind, now, buffer);
        return buffer[..count];
    }

    private static MemorySubject Player(int id) => new(SubjectKind.Player, (ulong)id);

    private static MemorySubject Creature(int id) => new(SubjectKind.Creature, (ulong)id);

    private static MemorySubject Place(int id) => new(SubjectKind.Place, (ulong)id);
}
