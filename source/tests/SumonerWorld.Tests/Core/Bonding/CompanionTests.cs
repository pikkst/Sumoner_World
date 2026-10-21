using System.Numerics;
using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Cognition.Behaviour;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.World;
using SumonerWorld.Scenarios;

namespace SumonerWorld.Tests.Core.Bonding;

public sealed class CompanionTests : IDisposable
{
    private const ulong Alice = 1001;
    private static readonly Vector3 Start = new(100, 0, 100);

    private readonly CreatureWorld _world = new(ShippedData.Current, new CreatureWorldSettings(), seed: 6);
    private readonly SimulationScheduler _scheduler;
    private readonly CreatureSystems _systems;
    private readonly Entity _alice;

    public CompanionTests()
    {
        _scheduler = new SimulationScheduler(_world.Clock, _world.Events);
        _systems = _world.CreateSystems();
        _systems.RegisterAll(_scheduler);
        _alice = _world.SpawnPlayerAvatar(Alice, Start);
    }

    public void Dispose() => _world.Dispose();

    [Fact]
    public void A_companion_follows_its_running_summoner_without_crowding_them()
    {
        Entity fox = Companion("bogfox", Start + new Vector3(3, 0, 0), loyalty: 60);
        float closest = float.MaxValue;

        for (int tick = 0; tick < 150; tick++)
        {
            MoveAlice(new Vector3(0, 0, 0.2f));
            _scheduler.RunTick();
            closest = MathF.Min(closest, Gap(fox));
        }

        Run(90);

        Assert.InRange(Gap(fox), 0.5f, CompanionSystem.FollowMetres + 0.5f);
        Assert.True(closest > 0.3f, $"it never pushed into its summoner ({closest:F2} m)");
        Assert.Equal((int)CompanionMode.Follow, _world.World.GetComponent<CompanionState>(fox).Mode);
    }

    [Fact]
    public void A_companion_left_far_behind_is_drawn_back_to_its_summoners_side()
    {
        Entity fox = Companion("bogfox", Start + new Vector3(3, 0, 0), loyalty: 60);
        Teleport(_alice, Start + new Vector3(0, 0, 150));

        Run(2);

        Assert.True(Gap(fox) < 4, $"it is back at her side ({Gap(fox):F1} m)");
    }

    [Fact]
    public void A_companion_defends_its_summoner_unprompted_and_lets_a_yielding_foe_go()
    {
        Entity companion = Companion("mossback", Start + new Vector3(3, 0, 0), loyalty: 60);
        Entity wild = Wild("bogfox", Start + new Vector3(-2, 0, 0));
        int before = Health(wild);

        _world.Events.Publish(new CombatHit(wild, _alice, 0, 5, 95));
        Run(120);

        Assert.Equal(wild, _world.World.GetComponent<CompanionState>(companion).Foe);
        Assert.True(_systems.Attacks.Started > 0, "the companion attacked");
        Assert.True(Health(wild) < before, "and hurt the attacker");

        _world.World.Add(wild, new Yielding(_world.Clock.Current.Index, _world.Clock.Current.Index, 0));
        Run(5);
        int started = _systems.Attacks.Started;
        Run(60);
        Assert.Equal(started, _systems.Attacks.Started);
        Assert.NotEqual((int)CompanionMode.Fight, _world.World.GetComponent<CompanionState>(companion).Mode);
    }

    [Fact]
    public void A_companion_helps_when_its_summoner_starts_a_fight_but_never_turns_on_its_own()
    {
        Entity companion = Companion("bogfox", Start + new Vector3(3, 0, 0), loyalty: 60);
        Entity sibling = Companion("mossback", Start + new Vector3(-3, 0, 0), loyalty: 60);
        Entity wild = Wild("mossback", Start + new Vector3(0, 0, 6));

        _world.Events.Publish(new CombatHit(_alice, sibling, 0, 5, 95));
        Run(2);
        Assert.Equal((int)CompanionMode.Follow, _world.World.GetComponent<CompanionState>(companion).Mode);

        _world.Events.Publish(new CombatHit(_alice, wild, 0, 5, 95));
        Run(2);
        Assert.Equal(wild, _world.World.GetComponent<CompanionState>(companion).Foe);
        Assert.Equal((int)CompanionMode.Fight, _world.World.GetComponent<CompanionState>(companion).Mode);
    }

    [Fact]
    public void A_companion_never_attacks_its_summoner_even_if_its_own_mind_wants_to()
    {
        Entity fox = Companion("bogfox", Start + new Vector3(1.5f, 0, 0), loyalty: 60);
        _world.World.GetComponent<FocusRef>(fox) = new FocusRef(_alice, 0, 1);
        int started = _systems.Attacks.Started;

        _world.Events.Publish(new CreatureIntent(fox, IntentKind.Attack, MoveTargetMode.Focus, 0, global::SumonerWorld.Core.Cognition.Appraisal.Emote.None, InteractVerb.Eat, null));
        _scheduler.RunTick();

        Assert.Equal(started, _systems.Attacks.Started);
    }

    [Theory]
    [InlineData(-80, true)]
    [InlineData(80, false)]
    public void An_independent_companion_wanders_off_when_its_summoner_stands_still_a_loyal_one_stays(int loyalty, bool roams)
    {
        Entity fox = Companion("bogfox", Start + new Vector3(3, 0, 0), loyalty: (sbyte)loyalty);

        Run((int)CompanionSystem.BoredAfterTicks + 30);

        Assert.Equal(roams, _world.World.GetComponent<CompanionState>(fox).Mode == (int)CompanionMode.Roam);
        Run(600);
        Assert.True(Gap(fox) <= CompanionSystem.LeashMetres + 2, $"it stays within the leash ({Gap(fox):F1} m)");

        MoveAlice(new Vector3(0, 0, 0.2f));
        Run(1);
        Assert.Equal((int)CompanionMode.Follow, _world.World.GetComponent<CompanionState>(fox).Mode);
    }

    [Fact]
    public void A_dismissed_companion_goes_back_to_its_own_mind()
    {
        Entity fox = Companion("bogfox", Start + new Vector3(3, 0, 0), loyalty: 60);
        Run(2);
        MindHandle mind = _world.World.GetComponent<MindRef>(fox).Handle;

        _world.Services.Bonding.Dismiss(mind, new MemorySubject(SubjectKind.Player, Alice));
        Run(1);

        Assert.False(_world.World.Has<CompanionState>(fox));
    }

    private Entity Companion(string species, Vector3 at, sbyte loyalty)
    {
        Entity creature = _world.SpawnCreature(_world.SpeciesIndex(species), at, new Personality(0, loyalty, 0, 0), group: 7);
        _world.Services.Bonding.Restore(
            _world.World.GetComponent<MindRef>(creature).Handle, new BondState(OwnershipState.Pacted, Alice, PactTerms.Of(0), 0, 1, true, 0, 0, false));
        return creature;
    }

    private Entity Wild(string species, Vector3 at) => _world.SpawnCreature(_world.SpeciesIndex(species), at, new Personality(0, 0, 0, 0), group: 8);

    private int Health(Entity creature) => _world.World.GetComponent<Combatant>(creature).Health;

    private float Gap(Entity creature)
    {
        Vector3 offset = _world.World.GetComponent<Position>(creature).Value - _world.World.GetComponent<Position>(_alice).Value;
        return new Vector2(offset.X, offset.Z).Length() - _world.Sizes.RadiusOf(creature) - _world.Sizes.RadiusOf(_alice);
    }

    private void MoveAlice(Vector3 step) => Teleport(_alice, _world.World.GetComponent<Position>(_alice).Value + step);

    private void Teleport(Entity entity, Vector3 to)
    {
        _world.World.GetComponent<Position>(entity) = new Position(to);
        _world.Grid.Move(entity, to);
    }

    private void Run(int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            _scheduler.RunTick();
        }
    }
}
