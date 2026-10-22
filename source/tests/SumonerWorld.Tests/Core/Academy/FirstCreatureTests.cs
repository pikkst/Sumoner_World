using System.Numerics;
using SumonerWorld.Core.Academy;
using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Progression;
using SumonerWorld.Core.Quests;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.Transactions;
using SumonerWorld.Core.World;
using SumonerWorld.Scenarios;

namespace SumonerWorld.Tests.Core.Academy;

public sealed class FirstCreatureTests : IDisposable
{
    private const ulong Alice = 1001;
    private const ulong Bob = 1002;

    private readonly CreatureWorld _world = new(ShippedData.Current, new CreatureWorldSettings(), seed: 66);
    private readonly SimulationScheduler _scheduler;
    private readonly TransactionProcessor _processor = new(new IntentDeduplicator());
    private readonly CooldownTracker _cooldowns = new();
    private readonly Entity _alice;
    private readonly Entity _bob;
    private readonly int _keeper;
    private uint _sequence;

    public FirstCreatureTests()
    {
        _scheduler = new SimulationScheduler(_world.Clock, _world.Events);
        _world.CreateSystems().RegisterAll(_scheduler);
        _keeper = RankingRules.NpcIndex(_world.Data, "elf_keeper");
        NpcDefinition keeper = Academy.Npcs[_keeper];
        _alice = _world.SpawnPlayerAvatar(Alice, new Vector3(keeper.X - 2, 0, keeper.Z));
        _bob = _world.SpawnPlayerAvatar(Bob, new Vector3(keeper.X - 2, 0, keeper.Z + 1));
        foreach (ulong account in new[] { Alice, Bob })
        {
            _world.Economy.Quests.Import(account, [new QuestSave("the_ranking", 0, [], Done: true)], _world.Data);
            _world.Economy.Academy.Import(account, new AcademySave([1, 1, 1, 1, 1, 1, 1, 1], 0, Ranked: true));
        }

        _world.World.GetComponent<PlayerProgress>(_alice).Rank = (int)Rank.D;
    }

    private AcademyDefinition Academy => _world.Data.Academy!;

    public void Dispose() => _world.Dispose();

    [Fact]
    public void The_answers_vote_for_the_species_and_a_tie_goes_to_the_roll()
    {
        var standing = new AcademyStanding();
        Array.Fill(standing.Answers, (byte)1);
        Assert.Equal("mossback", FirstCreatureRules.Species(Academy, standing, 0));

        Assert.Null(FirstCreatureRules.Species(Academy, new AcademyStanding(), 0));
        var tie = new AcademyStanding();
        tie.Answers[0] = 1; // mossback
        tie.Answers[1] = 1; // hollow owl
        Assert.Equal("hollow_owl", FirstCreatureRules.Species(Academy, tie, 0));
        Assert.Equal("mossback", FirstCreatureRules.Species(Academy, tie, 1));
    }

    [Fact]
    public void A_dwarf_gets_a_dwarf_creature_from_keeper_borra_and_lisel_brings_nothing()
    {
        _world.World.GetComponent<PlayerProgress>(_alice).Class = (int)SummonerClass.Dwarf;
        Ticks(16);
        int borra = RankingRules.NpcIndex(_world.Data, "dwarf_keeper");
        NpcDefinition keeper = Academy.Npcs[borra];

        Assert.Equal(0, Run(new TalkTransaction(_world, _alice, _keeper)).Detail);
        _world.World.GetComponent<Position>(_alice) = new Position(new Vector3(keeper.X - 2, 0, keeper.Z));
        IntentResult talk = Run(new TalkTransaction(_world, _alice, borra));

        Assert.Equal(TalkResult.Brought, talk.Detail);
        Assert.True(_world.TryFindCreature(new StableId(_world.Economy.Academy.Of(Alice).FirstCreature), out Entity creature));
        Assert.Equal("cinder_mole", _world.Data.Species[_world.World.GetComponent<SpeciesRef>(creature).Index].Id);
        Ticks(1);
        Assert.Equal(1, _world.Economy.Quests.Of(Alice).Find(Quest(Academy.FirstFriendQuest))!.Step);
    }

    [Fact]
    public void The_keeper_brings_a_young_creature_kept_for_the_student_whose_starter_pact_always_succeeds()
    {
        Ticks(16);
        int quest = Quest(Academy.FirstFriendQuest);
        Assert.Equal(QuestStatus.Active, _world.Economy.Quests.Of(Alice).StatusOf(quest));

        IntentResult talk = Run(new TalkTransaction(_world, _alice, _keeper));
        Assert.Equal(TalkResult.Brought, talk.Detail);
        var id = new StableId(_world.Economy.Academy.Of(Alice).FirstCreature);
        Assert.True(_world.TryFindCreature(id, out Entity creature));
        Assert.Equal("mossback", _world.Data.Species[_world.World.GetComponent<SpeciesRef>(creature).Index].Id);
        CreatureGrowth growth = _world.World.GetComponent<CreatureGrowth>(creature);
        Assert.Equal(((int)Rank.D, 1, (int)LifeStage.Young), (growth.Tier, growth.Level, growth.Stage));
        Assert.Equal(Alice, _world.Economy.Academy.ReservedFor(id.Value));
        Assert.Equal(0, Run(new TalkTransaction(_world, _alice, _keeper)).Detail);
        Ticks(1);

        Assert.Equal((byte)BondingOutcome.Refused, Run(new OfferPactTransaction(_world, _cooldowns, _bob, id, PactTerms.Of(0))).Detail);
        Assert.True(Run(new FeedTransaction(_world, _cooldowns, _alice, id)).IsApplied);
        Ticks(1);
        Assert.Equal((byte)BondingOutcome.Accepted, Run(new OfferPactTransaction(_world, _cooldowns, _alice, id, PactTerms.Of(0)), _world.Clock.Current.Index).Detail);
        Ticks(16);

        BondState bond = _world.Services.Bonding.GetBond(_world.World.GetComponent<MindRef>(creature).Handle);
        Assert.Equal((OwnershipState.Pacted, Alice), (bond.State, bond.Owner));
        Assert.Equal(1, Memories(creature, "FirstBond"));
        Assert.Equal(QuestStatus.Done, _world.Economy.Quests.Of(Alice).StatusOf(quest));
    }

    [Fact]
    public void Each_student_gets_their_own_creature_and_it_is_saved_with_their_standing()
    {
        Ticks(16);
        Run(new TalkTransaction(_world, _alice, _keeper));
        Run(new TalkTransaction(_world, _bob, _keeper));

        ulong alices = _world.Economy.Academy.Of(Alice).FirstCreature;
        ulong bobs = _world.Economy.Academy.Of(Bob).FirstCreature;
        Assert.NotEqual(0UL, alices);
        Assert.NotEqual(alices, bobs);

        AcademySave saved = _world.Economy.Academy.Export(Alice)!;
        _world.Economy.Academy.Import(3003, saved);
        Assert.Equal(alices, _world.Economy.Academy.Of(3003).FirstCreature);
    }

    private int Memories(Entity creature, string key)
    {
        var buffer = new RecalledMemory[64];
        int count = _world.Minds.RecallAbout(
            _world.World.GetComponent<MindRef>(creature).Handle, new MemorySubject(SubjectKind.Player, Alice), _world.Clock.Current.Index, buffer);
        ushort type = _world.Rules.FindByKey(key)!.Value;
        return buffer.Take(count).Count(memory => memory.Record.EventType == type);
    }

    private int Quest(string id) => _world.Data.Quests.Select((quest, i) => (quest, i)).Single(item => item.quest.Id == id).i;

    private void Ticks(int count)
    {
        for (int i = 0; i < count; i++)
        {
            _scheduler.RunTick();
        }
    }

    private IntentResult Run<T>(T transaction, ulong now = 0)
        where T : struct, ITransaction
    {
        return _processor.Execute(new IntentId(2, ++_sequence), ref transaction, now);
    }
}
