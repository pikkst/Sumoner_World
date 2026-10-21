using System.Numerics;
using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Cognition.Behaviour;
using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Progression;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.Transactions;
using SumonerWorld.Core.World;
using SumonerWorld.Scenarios;

namespace SumonerWorld.Tests.Core.Progression;

public sealed class SkillTests : IDisposable
{
    private const ulong Alice = 1001;
    private static readonly Vector3 Start = new(100, 0, 100);

    private readonly CreatureWorld _world = new(ShippedData.Current, new CreatureWorldSettings(), seed: 31);
    private readonly SimulationScheduler _scheduler;
    private readonly CreatureSystems _systems;
    private readonly TransactionProcessor _processor = new(new IntentDeduplicator());
    private readonly Entity _alice;
    private uint _sequence;

    public SkillTests()
    {
        _scheduler = new SimulationScheduler(_world.Clock, _world.Events);
        _systems = _world.CreateSystems();
        _systems.RegisterAll(_scheduler);
        _alice = _world.SpawnPlayerAvatar(Alice, Start);
    }

    public void Dispose() => _world.Dispose();

    [Fact]
    public void Points_come_every_two_levels_and_slots_grow_with_the_stage()
    {
        Assert.Equal(2, SkillRules.Points(new CreatureGrowth(0, 5, 0, 0, 0, 0, 0), default));
        Assert.Equal(1, SkillRules.Points(new CreatureGrowth(0, 5, 0, 0, 0, 0, 0), new CreatureSkills(1, 0, 0, 1, 0)));
        Assert.Equal((2, 3, 4), (SkillRules.Slots(LifeStage.Young), SkillRules.Slots(LifeStage.Grown), SkillRules.Slots(LifeStage.Elder)));
    }

    [Fact]
    public void A_branch_is_liked_for_the_traits_it_wants_and_the_roles_the_creature_did_most()
    {
        SkillTreeDefinition tree = Tree("bogfox");
        SkillBranchDefinition scout = tree.Branches.Single(branch => branch.Id == "scout");
        SkillBranchDefinition pack = tree.Branches.Single(branch => branch.Id == "pack");
        var curiousWalker = new CreatureGrowth(0, 3, 0, 0, 0, 100, 0);

        Assert.True(SkillRules.Affinity(scout, new Personality(0, 0, 90, 0), curiousWalker) >= SkillRules.WarmAt);
        Assert.True(SkillRules.Affinity(pack, new Personality(0, -80, 0, 0), curiousWalker) <= SkillRules.ColdAt);
        Assert.Equal((1, 1, 2), (SkillRules.Cost(SkillRules.WarmAt), SkillRules.Cost(0), SkillRules.Cost(SkillRules.ColdAt)));
    }

    [Fact]
    public void The_owner_teaches_an_available_skill_and_a_passive_raises_the_stats()
    {
        Entity fox = Companion("bogfox", level: 5, loyalty: 0);
        int calmHealth = _world.World.GetComponent<Combatant>(fox).MaxHealth;
        int lunge = SkillRules.IndexOf(Tree("bogfox"), "lunge");
        int packHeart = SkillRules.IndexOf(Tree("bogfox"), "pack_heart");

        Assert.Equal(SkillState.NeedsSkill, (SkillState)Learn(fox, packHeart).Detail);
        Assert.True(Learn(fox, lunge).IsApplied);
        Assert.True(Learn(fox, packHeart).IsApplied);
        Assert.Equal(SkillState.Learned, SkillLearning.StateOf(_world, fox, lunge));
        Assert.Equal(calmHealth * 110 / 100, _world.World.GetComponent<Combatant>(fox).MaxHealth);
        Assert.Equal((IntentRejectReason.WrongState, SkillState.NoPoints), Refusal(Learn(fox, SkillRules.IndexOf(Tree("bogfox"), "feint"))));
    }

    [Fact]
    public void Only_the_owner_teaches_and_only_what_the_creature_is_ready_for()
    {
        Entity fox = Companion("bogfox", level: 2, loyalty: 0);
        Entity wild = _world.SpawnCreature(_world.SpeciesIndex("bogfox"), Start + new Vector3(5, 0, 0), new Personality(0, 0, 0, 0), group: 3);

        Assert.Equal(IntentRejectReason.NotOwner, Learn(wild, 0).Reason);
        Assert.Equal((IntentRejectReason.WrongState, SkillState.NeedsLevel), Refusal(Learn(fox, SkillRules.IndexOf(Tree("bogfox"), "lunge"))));
        Assert.Equal((IntentRejectReason.InvalidArguments, SkillState.Unknown), Refusal(Learn(fox, 30)));
    }

    [Fact]
    public void A_skill_from_a_disliked_branch_costs_two_points_and_the_creature_shows_reluctance()
    {
        Entity fox = Companion("bogfox", level: 6, loyalty: -100);
        int lunge = SkillRules.IndexOf(Tree("bogfox"), "lunge");

        Assert.True(Learn(fox, lunge).IsApplied);
        _scheduler.RunTick();

        Assert.Equal(2, _world.World.GetComponent<CreatureSkills>(fox).Spent);
        Assert.Equal(Emote.Grudge, _world.World.GetComponent<CurrentEmote>(fox).Value);
    }

    [Fact]
    public void A_learned_move_is_used_in_a_fight_when_it_reaches()
    {
        Entity fox = Companion("bogfox", level: 6, loyalty: 0, summoned: false);
        Assert.True(Learn(fox, SkillRules.IndexOf(Tree("bogfox"), "lunge")).IsApplied);
        Entity target = _world.SpawnCreature(_world.SpeciesIndex("mossback"), _world.World.GetComponent<Position>(fox).Value + new Vector3(0, 0, 4.5f), new Personality(0, 0, 0, 0), group: 3);
        _world.World.GetComponent<FocusRef>(fox) = new FocusRef(target, 0, 1);

        _world.Events.Publish(new CreatureIntent(fox, IntentKind.Attack, MoveTargetMode.Focus, 0, Emote.None, InteractVerb.Eat, null));
        _scheduler.RunTick();

        Assert.Equal(_world.Data.MoveIndex("lunge"), _world.World.GetComponent<ActiveMove>(fox).Move);
    }

    [Fact]
    public void A_creature_whose_points_go_unspent_for_three_days_picks_from_the_branch_it_likes_most()
    {
        Entity fox = Companion("bogfox", level: 2, loyalty: 100, summoned: false);
        var skillsOnly = new SimulationScheduler(_world.Clock, _world.Events);
        skillsOnly.Register(new SkillSystem(_world));
        int ticks = (SkillSystem.PickAfterDays * SimulationSettings.DefaultTicksPerDay) + 600;

        for (int tick = 0; tick < ticks; tick++)
        {
            skillsOnly.RunTick();
        }

        CreatureSkills skills = _world.World.GetComponent<CreatureSkills>(fox);
        Assert.Equal(1, skills.PickedItself);
        Assert.True(skills.Knows(SkillRules.IndexOf(Tree("bogfox"), "long_legs")) || skills.Knows(SkillRules.IndexOf(Tree("bogfox"), "feint")) || skills.Knows(SkillRules.IndexOf(Tree("bogfox"), "lunge")));
        Assert.Equal(0, SkillRules.Points(_world.World.GetComponent<CreatureGrowth>(fox), skills));
    }

    private static (IntentRejectReason, SkillState) Refusal(IntentResult result) => (result.Reason, (SkillState)result.Detail);

    private SkillTreeDefinition Tree(string species) => _world.Data.SkillTreeOf(_world.SpeciesIndex(species))!;

    private Entity Companion(string species, int level, sbyte loyalty, bool summoned = true)
    {
        Entity creature = _world.SpawnCreature(_world.SpeciesIndex(species), Start + new Vector3(2, 0, 0), new Personality(0, loyalty, 0, 0), group: 7);
        ref CreatureGrowth growth = ref _world.World.GetComponent<CreatureGrowth>(creature);
        growth = growth with { Level = level, Stage = (int)LifeStage.Grown };
        ProgressionComponents.Restat(_world.Growth, _world.Data, _world.World, creature, _world.Data.Species[_world.SpeciesIndex(species)].Combat, ref _world.World.GetComponent<Combatant>(creature));
        _world.Services.Bonding.Restore(
            _world.World.GetComponent<MindRef>(creature).Handle, new BondState(OwnershipState.Pacted, Alice, PactTerms.Of(0), 0, 3, summoned, 0, 0, false));
        return creature;
    }

    private IntentResult Learn(Entity creature, int node)
    {
        var transaction = new LearnSkillTransaction(_world, _alice, _world.World.GetComponent<StableId>(creature), node);
        return _processor.Execute(new IntentId(1, ++_sequence), ref transaction, _world.Clock.Current.Index);
    }
}
