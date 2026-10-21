using System.Numerics;
using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.Transactions;
using SumonerWorld.Core.World;
using SumonerWorld.Scenarios;

namespace SumonerWorld.Tests.Core.Bonding;

public sealed class CommandTests : IDisposable
{
    private const ulong Alice = 1001;
    private static readonly Vector3 Start = new(100, 0, 100);

    private readonly CreatureWorld _world = new(ShippedData.Current, new CreatureWorldSettings(), seed: 8);
    private readonly SimulationScheduler _scheduler;
    private readonly CreatureSystems _systems;
    private readonly TransactionProcessor _processor = new(new IntentDeduplicator());
    private readonly Entity _alice;
    private uint _sequence;

    public CommandTests()
    {
        _scheduler = new SimulationScheduler(_world.Clock, _world.Events);
        _systems = _world.CreateSystems();
        _systems.RegisterAll(_scheduler);
        _alice = _world.SpawnPlayerAvatar(Alice, Start);
    }

    public void Dispose() => _world.Dispose();

    [Fact]
    public void Commands_need_a_companion_with_the_player_and_an_attack_needs_a_foreign_creature_in_range()
    {
        Entity wild = Wild("bogfox", Start + new Vector3(0, 0, 8));
        Assert.Equal(IntentRejectReason.WrongState, Command(CompanionCommand.GuardMe).Reason);

        Entity companion = Companion("mossback", Start + new Vector3(3, 0, 0), loyalty: 100, bondLevel: 4);
        Entity far = Wild("bogfox", Start + new Vector3(0, 0, 60));

        Assert.Equal(IntentRejectReason.InvalidArguments, Command((CompanionCommand)9).Reason);
        Assert.Equal(IntentRejectReason.InvalidTarget, Command(CompanionCommand.Attack, companion).Reason);
        Assert.Equal(IntentRejectReason.InvalidTarget, Command(CompanionCommand.Attack, default).Reason);
        Assert.Equal(IntentRejectReason.OutOfRange, Command(CompanionCommand.Attack, far).Reason);
        Assert.True(Command(CompanionCommand.Attack, wild).IsApplied);
    }

    [Fact]
    public void A_devoted_companion_obeys_an_attack_shows_trust_and_fights_the_target()
    {
        Entity companion = Companion("mossback", Start + new Vector3(3, 0, 0), loyalty: 100, bondLevel: 4);
        Entity wild = Wild("bogfox", Start + new Vector3(0, 0, 8));

        IntentResult result = Command(CompanionCommand.Attack, wild);
        Run(1);

        Assert.Equal(1, result.Detail & 0xF);
        CompanionOrder order = _world.World.GetComponent<CompanionOrder>(companion);
        Assert.Equal(CommandResponse.Obey, (CommandResponse)order.Response);
        Assert.Equal(ObedienceReason.Bond, (ObedienceReason)order.Reason);
        Assert.Equal(Emote.Trust, Shown(companion));
        Assert.Equal(wild, _world.World.GetComponent<CompanionState>(companion).Foe);

        Run(150);
        Assert.True(_systems.Attacks.Started > 0, "it attacked the target");
    }

    [Fact]
    public void An_independent_companion_refuses_a_suicidal_attack_with_a_grudge_and_keeps_to_itself()
    {
        Entity companion = Companion("bogfox", Start + new Vector3(3, 0, 0), loyalty: -100, bondLevel: 0);
        Entity wild = Wild("mossback", Start + new Vector3(0, 0, 8));
        _world.World.GetComponent<Combatant>(wild) = _world.World.GetComponent<Combatant>(wild) with { Power = 1000 };

        IntentResult result = Command(CompanionCommand.Attack, wild);
        Run(1);

        Assert.Equal(1, result.Detail >> 4);
        CompanionOrder order = _world.World.GetComponent<CompanionOrder>(companion);
        Assert.Equal((CommandResponse.Refuse, ObedienceReason.Suicidal), ((CommandResponse)order.Response, (ObedienceReason)order.Reason));
        Assert.Equal(Emote.Grudge, Shown(companion));
        Run((int)CompanionSystem.ShowAnswerTicks + 30);
        Assert.Equal(0, _systems.Attacks.Started);
        Assert.Equal(0, _world.World.GetComponent<CompanionOrder>(companion).Active);
    }

    [Fact]
    public void A_hesitating_companion_pauses_before_it_comes_back()
    {
        Entity companion = Companion("bogfox", Start + new Vector3(12, 0, 0), loyalty: 0, bondLevel: 0);
        Run(1);
        Order(companion, CompanionCommand.Retreat, CommandResponse.Hesitate, ObedienceReason.Fear);
        Vector3 before = Position(companion);

        Run((int)CompanionSystem.HesitateTicks - 2);
        Assert.Equal(Emote.Fear, Shown(companion));
        Assert.True(Vector3.Distance(before, Position(companion)) < 0.3f, "it stays put while it hesitates");

        Run(90);
        Assert.True(Gap(companion) < 8, $"then it comes back ({Gap(companion):F1} m)");
    }

    [Fact]
    public void A_holding_companion_stays_where_it_was_told_while_its_summoner_walks_away()
    {
        Entity companion = Companion("bogfox", Start + new Vector3(3, 0, 0), loyalty: 100, bondLevel: 4);
        Run(1);
        Assert.True(Command(CompanionCommand.Hold).IsApplied);
        Vector3 place = Position(companion);

        for (int i = 0; i < 200; i++)
        {
            MoveAlice(new Vector3(0, 0, 0.2f));
            _scheduler.RunTick();
        }

        Assert.True(Vector3.Distance(place, Position(companion)) < 1.5f, "it held its place");
        Assert.True(Gap(companion) > 30, "and did not follow");

        Assert.True(Command(CompanionCommand.Retreat).IsApplied);
        Run(300);
        Assert.True(Gap(companion) < CompanionSystem.FollowMetres + 1, $"called back, it comes ({Gap(companion):F1} m)");
    }

    [Fact]
    public void A_guarding_companion_stays_close_and_does_not_wander_even_when_independent()
    {
        Entity companion = Companion("bogfox", Start + new Vector3(3, 0, 0), loyalty: -60, bondLevel: 4);
        Run(1);
        Assert.True(Command(CompanionCommand.GuardMe).IsApplied);
        Assert.NotEqual(CommandResponse.Refuse, (CommandResponse)_world.World.GetComponent<CompanionOrder>(companion).Response);

        Run((int)CompanionSystem.BoredAfterTicks + 120);

        Assert.NotEqual((int)CompanionMode.Roam, _world.World.GetComponent<CompanionState>(companion).Mode);
        Assert.True(Gap(companion) <= CompanionSystem.GuardMetres + 0.5f, $"it stays close ({Gap(companion):F1} m)");
    }

    [Fact]
    public void A_retreating_companion_leaves_the_fight_and_ignores_new_blows_for_a_while()
    {
        Entity companion = Companion("mossback", Start + new Vector3(3, 0, 0), loyalty: 100, bondLevel: 4);
        Entity wild = Wild("bogfox", Start + new Vector3(0, 0, 8));
        Command(CompanionCommand.Attack, wild);
        Run(10);
        Assert.Equal((int)CompanionMode.Fight, _world.World.GetComponent<CompanionState>(companion).Mode);

        Assert.True(Command(CompanionCommand.Retreat).IsApplied);
        Run(2);
        _world.Events.Publish(new CombatHit(wild, companion, 0, 5, 95));
        int started = _systems.Attacks.Started;
        Run(60);

        Assert.NotEqual((int)CompanionMode.Fight, _world.World.GetComponent<CompanionState>(companion).Mode);
        Assert.Equal(started, _systems.Attacks.Started);
    }

    [Fact]
    public void The_inspector_shows_the_last_command_and_why()
    {
        Entity companion = Companion("mossback", Start + new Vector3(3, 0, 0), loyalty: 100, bondLevel: 4);
        Command(CompanionCommand.GuardMe);
        Run(30);

        InspectedCommand? shown = new CreatureInspector(_world).Inspect(_world.World.GetComponent<StableId>(companion))!.Command;

        Assert.Equal(new InspectedCommand(CompanionCommand.GuardMe, CommandResponse.Obey, shown!.Value.Obedience, ObedienceReason.Bond, 30), shown);
    }

    private IntentResult Command(CompanionCommand command, Entity target = default)
    {
        StableId id = _world.World.IsAlive(target) && _world.World.Has<StableId>(target) ? _world.World.GetComponent<StableId>(target) : new StableId(0);
        var transaction = new CommandTransaction(_world, _alice, command, id);
        return _processor.Execute(new IntentId(1, ++_sequence), ref transaction, _world.Clock.Current.Index);
    }

    private void Order(Entity companion, CompanionCommand command, CommandResponse response, ObedienceReason reason)
    {
        var order = new CompanionOrder(default, _world.Clock.Current.Index, Position(companion), (int)command, (int)response, 300, (int)reason, 1);
        _world.World.Add(companion, order);
    }

    private Entity Companion(string species, Vector3 at, sbyte loyalty, byte bondLevel)
    {
        Entity creature = _world.SpawnCreature(_world.SpeciesIndex(species), at, new Personality(0, loyalty, 0, 0), group: 7);
        _world.Services.Bonding.Restore(
            _world.World.GetComponent<MindRef>(creature).Handle, new BondState(OwnershipState.Pacted, Alice, PactTerms.Of(0), 0, bondLevel, true, 0, 0, false));
        return creature;
    }

    private Entity Wild(string species, Vector3 at) => _world.SpawnCreature(_world.SpeciesIndex(species), at, new Personality(0, 0, 0, 0), group: 8);

    private Emote Shown(Entity creature) => _world.World.GetComponent<CurrentEmote>(creature).Value;

    private Vector3 Position(Entity creature) => _world.World.GetComponent<Position>(creature).Value;

    private float Gap(Entity creature)
    {
        Vector3 offset = Position(creature) - _world.World.GetComponent<Position>(_alice).Value;
        return new Vector2(offset.X, offset.Z).Length() - _world.Sizes.RadiusOf(creature) - _world.Sizes.RadiusOf(_alice);
    }

    private void MoveAlice(Vector3 step)
    {
        Vector3 to = _world.World.GetComponent<Position>(_alice).Value + step;
        _world.World.GetComponent<Position>(_alice) = new Position(to);
        _world.Grid.Move(_alice, to);
    }

    private void Run(int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            _scheduler.RunTick();
        }
    }
}
