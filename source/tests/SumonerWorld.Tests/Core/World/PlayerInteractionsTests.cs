using System.Numerics;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.World;
using SumonerWorld.Scenarios;

namespace SumonerWorld.Tests.Core.World;

public class PlayerInteractionsTests
{
    private const ulong Alice = 1001;
    private static readonly MemorySubject AliceSubject = new(SubjectKind.Player, Alice);

    [Fact]
    public void Feeding_leaves_a_memory_less_hunger_and_waits_for_the_cooldown()
    {
        using var grove = new Grove();
        Entity calf = grove.Creature("mossback", new Vector3(103, 0, 100));
        grove.World.Minds.SetNeed(grove.Mind(calf), Need.Hunger, 80, 0);

        IntentResult fed = grove.Feed(calf, now: 10);
        IntentResult tooSoon = grove.Feed(calf, now: 20);

        Assert.True(fed.IsApplied);
        Assert.Equal(IntentRejectReason.OnCooldown, tooSoon.Reason);
        Assert.Equal(80 - PlayerInteractions.MealSize, grove.World.Minds.GetNeed(grove.Mind(calf), Need.Hunger, 10));
        Assert.Single(grove.MemoriesAbout(calf), memory => memory.Record.EventType == grove.Event("FedBy"));
        Assert.Equal(new PlayerInteracted(Alice, InteractionKind.Feed, grove.World.SpeciesIndex("mossback"), 1), Assert.Single(grove.World.Events.Read<PlayerInteracted>().ToArray()));
        Assert.Equal(grove.Event("FedBy"), Assert.Single(grove.World.Events.Read<MemoryFormed>().ToArray()).EventType);

        Entity far = grove.Creature("mossback", new Vector3(110, 0, 100));
        Assert.Equal(IntentRejectReason.OutOfRange, grove.Feed(far, now: 200).Reason);
    }

    [Fact]
    public void Hits_wear_a_creature_down_until_it_dies_and_kin_nearby_remember_who_did_it()
    {
        // Combat spec (M2-01) replaced the M1 placeholder "the third hit kills": hits now deal damage until health runs out.
        using var grove = new Grove();
        Entity victim = grove.Creature("mossback", new Vector3(103, 0, 100));
        grove.World.Snare(victim, 0); // keeps it in reach: a hurt creature runs away
        Entity kin = grove.Creature("mossback", new Vector3(120, 0, 100));
        Entity farKin = grove.Creature("mossback", new Vector3(160, 0, 100));
        Entity fox = grove.Creature("bogfox", new Vector3(110, 0, 110));
        StableId victimId = grove.World.World.GetComponent<StableId>(victim);

        int swings = 0;
        for (int tick = 0; tick < 3_000 && grove.World.TryFindCreature(victimId, out _); tick++)
        {
            swings += grove.Hit(victim, grove.Now).IsApplied ? 1 : 0;
            grove.Run(1);
        }

        Assert.False(grove.World.TryFindCreature(victimId, out _));
        Assert.InRange(swings, 20, 40);
        Assert.Contains(grove.MemoriesAbout(kin), memory => memory.Record.EventType == grove.Event("KilledKin") && memory.Record.IsPinned);
        Assert.Empty(grove.MemoriesAbout(farKin));
        Assert.Empty(grove.MemoriesAbout(fox));
        Assert.Contains(grove.World.Appraisal.Appraise(grove.Mind(kin), AliceSubject, grove.Now), new[] { Emote.Fear, Emote.Grudge });
    }

    [Fact]
    public void A_snared_creature_cannot_move_and_the_one_who_frees_it_gets_a_core_memory()
    {
        using var grove = new Grove();
        Entity stuck = grove.Creature("bogfox", new Vector3(102.5f, 0, 100));
        Entity free = grove.Creature("bogfox", new Vector3(98, 0, 100));
        Assert.Equal(IntentRejectReason.WrongState, grove.Help(free, now: 0).Reason);

        grove.World.Snare(stuck, now: 0);
        Vector3 before = grove.Position(stuck);
        grove.World.Paths.Request(stuck, before, before + new Vector3(10, 0, 0), 0.5f);
        grove.World.Paths.Submit(grove.World.Jobs);
        grove.Run(30);
        Assert.Equal(before, grove.Position(stuck));
        Assert.Equal(Emote.Fear, grove.World.World.GetComponent<CurrentEmote>(stuck).Value);

        IntentResult helped = grove.Help(stuck, now: grove.Now);

        Assert.True(helped.IsApplied);
        Assert.False(grove.World.World.Has<Snared>(stuck));
        RecalledMemory saved = Assert.Single(grove.MemoriesAbout(stuck), memory => memory.Record.EventType == grove.Event("SavedLifeOf"));
        Assert.True(saved.Record.IsPinned, "A core memory.");
        Assert.True(grove.World.Opinions.GetOpinion(grove.Mind(stuck), AliceSubject, grove.Now).Trust > 0);
    }

    [Fact]
    public void A_shout_frightens_the_predators_in_reach_and_nothing_else()
    {
        using var grove = new Grove();
        Entity near = grove.Creature("bogfox", new Vector3(108, 0, 100));
        Entity far = grove.Creature("bogfox", new Vector3(130, 0, 100));
        Entity grazer = grove.Creature("mossback", new Vector3(104, 0, 100));

        IntentResult shout = grove.Scare(now: 5);

        Assert.Equal(IntentResult.Applied(1), shout);
        Assert.Contains(grove.MemoriesAbout(near), memory => memory.Record.EventType == grove.Event("AttackedBy"));
        Assert.True(grove.World.Appraisal.GetEmotions(grove.Mind(near), 5).Fear >= 60);
        Assert.Empty(grove.MemoriesAbout(far));
        Assert.Empty(grove.MemoriesAbout(grazer));
        Assert.Equal(IntentRejectReason.OnCooldown, grove.Scare(now: 30).Reason);
    }

    [Fact]
    public void A_creature_fed_again_and_again_comes_to_trust_the_player()
    {
        using var grove = new Grove();
        Entity calf = grove.Creature("mossback", new Vector3(103, 0, 100));
        for (ulong meal = 0; meal < 8; meal++)
        {
            Assert.True(grove.Feed(calf, now: meal * FeedTransaction.CooldownTicks).IsApplied);
        }

        ulong now = 8 * FeedTransaction.CooldownTicks;
        RecalledMemory fed = Assert.Single(grove.MemoriesAbout(calf));
        Assert.True(grove.World.Opinions.GetOpinion(grove.Mind(calf), AliceSubject, now).Trust > 0);
        Assert.Contains(grove.World.Appraisal.Appraise(grove.Mind(calf), AliceSubject, now), new[] { Emote.Trust, Emote.Affection });
    }

    private sealed class Grove : IDisposable
    {
        private readonly CooldownTracker _cooldowns = new();
        private readonly SimulationScheduler _scheduler;
        private readonly Entity _avatar;

        public Grove()
        {
            World = new CreatureWorld(ShippedData.Current, new CreatureWorldSettings(), seed: 23);
            _scheduler = new SimulationScheduler(World.Clock, World.Events);
            CreatureSystems systems = World.CreateSystems();
            _scheduler.Register(systems.JobApply);
            _scheduler.Register(systems.Movement);
            _scheduler.Register(systems.Combat);
            _scheduler.Register(systems.Vitals);
            _avatar = World.SpawnPlayerAvatar(Alice, new Vector3(100, 0, 100));
        }

        public CreatureWorld World { get; }

        // Memories are read after every interaction in a test (they decay little in 1,000 ticks).
        public const ulong Later = 1_000;

        public ulong Now => World.Clock.Current.Index;

        public Entity Creature(string species, Vector3 at) =>
            World.SpawnCreature(World.SpeciesIndex(species), at, new Personality(-20, 0, 0, 0), group: 0);

        public MindHandle Mind(Entity creature) => World.World.GetComponent<MindRef>(creature).Handle;

        public Vector3 Position(Entity entity) => World.World.GetComponent<Position>(entity).Value;

        public ushort Event(string key) => World.Rules.FindByKey(key)!.Value;

        public RecalledMemory[] MemoriesAbout(Entity creature)
        {
            var memories = new RecalledMemory[16];
            int count = World.Minds.RecallAbout(Mind(creature), AliceSubject, Later, memories);
            return memories[..count];
        }

        public IntentResult Feed(Entity creature, ulong now) => Run(new FeedTransaction(World, _cooldowns, _avatar, Id(creature)), now);

        public IntentResult Hit(Entity creature, ulong now) => Run(new HitTransaction(World, _avatar, Id(creature)), now);

        public IntentResult Help(Entity creature, ulong now) => Run(new HelpTransaction(World, _cooldowns, _avatar, Id(creature)), now);

        public IntentResult Scare(ulong now) => Run(new ScareTransaction(World, _cooldowns, _avatar), now);

        public void Run(int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                _scheduler.RunTick();
            }
        }

        public void Dispose() => World.Dispose();

        private StableId Id(Entity creature) => World.World.GetComponent<StableId>(creature);

        private static IntentResult Run<T>(T transaction, ulong now)
            where T : struct, SumonerWorld.Core.Transactions.ITransaction
        {
            IntentResult valid = transaction.Validate(now);
            return valid.IsApplied ? transaction.Commit(now) : valid;
        }
    }
}
