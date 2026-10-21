using System.Numerics;
using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Progression;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.Transactions;
using SumonerWorld.Core.World;
using SumonerWorld.Scenarios;

namespace SumonerWorld.Tests.Core.Bonding;

public sealed class SkillCommandTests : IDisposable
{
    private const ulong Alice = 1001;
    private static readonly Vector3 Start = new(100, 0, 100);

    private readonly CreatureWorld _world = new(ShippedData.Current, new CreatureWorldSettings(), seed: 9);
    private readonly SimulationScheduler _scheduler;
    private readonly TransactionProcessor _processor = new(new IntentDeduplicator());
    private readonly Entity _alice;
    private readonly int _hornToss;
    private uint _sequence;

    public SkillCommandTests()
    {
        _scheduler = new SimulationScheduler(_world.Clock, _world.Events);
        _world.CreateSystems().RegisterAll(_scheduler);
        _alice = _world.SpawnPlayerAvatar(Alice, Start);
        _hornToss = _world.Data.MoveIndex("horn_toss");
    }

    public void Dispose() => _world.Dispose();

    [Fact]
    public void Only_a_move_a_companion_at_ones_side_has_learned_can_be_called_for()
    {
        Entity wild = Wild(Start + new Vector3(0, 0, 8));
        Entity calf = Companion(learned: 0);

        IntentResult result = Call(wild, _hornToss);
        Assert.Equal((IntentRejectReason.WrongState, 1), (result.Reason, result.Detail));
        Assert.Equal(IntentRejectReason.InvalidArguments, Call(wild, 999).Reason);
        Assert.Equal(IntentRejectReason.InvalidTarget, Call(calf, _hornToss).Reason);

        _world.World.GetComponent<CreatureSkills>(calf) = _world.World.GetComponent<CreatureSkills>(calf) with { Learned = 1 };
        Assert.True(SkillCommandTransaction.KnowsMove(_world, calf, _hornToss));
        Assert.True(Call(wild, _hornToss).IsApplied);
        Assert.Equal(_hornToss, _world.World.GetComponent<RequestedMove>(calf).Move);
    }

    [Fact]
    public void A_companion_that_obeys_uses_the_called_for_move_first_and_then_fights_as_it_likes()
    {
        Entity wild = Wild(Start + new Vector3(0, 0, 6));
        Entity calf = Companion(learned: 1);

        Assert.True(Call(wild, _hornToss).IsApplied);
        int first = -1;
        for (int tick = 0; tick < 300 && first < 0; tick++)
        {
            _scheduler.RunTick();
            first = _world.World.Has<ActiveMove>(calf) ? _world.World.GetComponent<ActiveMove>(calf).Move : -1;
        }

        Assert.Equal(_hornToss, first);
        Assert.False(_world.World.Has<RequestedMove>(calf));
    }

    [Fact]
    public void An_old_request_is_forgotten_and_the_companion_fights_as_it_likes()
    {
        Entity wild = Wild(Start + new Vector3(0, 0, 6));
        Entity calf = Companion(learned: 1);
        _world.World.Add(calf, new RequestedMove(_world.Clock.Current.Index, _world.Data.MoveIndex("stomp"), 0));
        _scheduler.RunTick();
        var attack = new CommandTransaction(_world, _alice, CompanionCommand.Attack, _world.World.GetComponent<StableId>(wild));
        Assert.True(_processor.Execute(new IntentId(1, ++_sequence), ref attack, _world.Clock.Current.Index).IsApplied);

        int first = -1;
        for (int tick = 0; tick < 300 && first < 0; tick++)
        {
            _scheduler.RunTick();
            first = _world.World.Has<ActiveMove>(calf) ? _world.World.GetComponent<ActiveMove>(calf).Move : -1;
        }

        Assert.NotEqual(-1, first);
        Assert.NotEqual(_world.Data.MoveIndex("stomp"), first);
        Assert.False(_world.World.Has<RequestedMove>(calf));
    }

    private IntentResult Call(Entity target, int move)
    {
        var transaction = new SkillCommandTransaction(_world, _alice, _world.World.GetComponent<StableId>(target), move);
        return _processor.Execute(new IntentId(1, ++_sequence), ref transaction, _world.Clock.Current.Index);
    }

    private Entity Companion(ulong learned)
    {
        Entity creature = _world.SpawnCreature(_world.SpeciesIndex("mossback"), Start + new Vector3(3, 0, 0), new Personality(0, 100, 0, 0), group: 7);
        _world.Services.Bonding.Restore(
            _world.World.GetComponent<MindRef>(creature).Handle, new BondState(OwnershipState.Pacted, Alice, PactTerms.Of(0), 0, 4, true, 0, 0, false));
        ref CreatureGrowth growth = ref _world.World.GetComponent<CreatureGrowth>(creature);
        growth.Level = Math.Max(growth.Level, 4);
        _world.World.GetComponent<CreatureSkills>(creature) = _world.World.GetComponent<CreatureSkills>(creature) with { Learned = learned };
        return creature;
    }

    private Entity Wild(Vector3 at) => _world.SpawnCreature(_world.SpeciesIndex("bogfox"), at, new Personality(0, 0, 0, 0), group: 8);
}
