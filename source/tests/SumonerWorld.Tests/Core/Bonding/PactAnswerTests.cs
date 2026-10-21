using System.Numerics;
using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.Transactions;
using SumonerWorld.Core.World;
using SumonerWorld.Scenarios;

namespace SumonerWorld.Tests.Core.Bonding;

public sealed class PactAnswerTests : IDisposable
{
    private const ulong Alice = 1001;
    private static readonly Vector3 Player = new(100, 0, 100);

    private readonly CreatureWorld _world = new(ShippedData.Current, new CreatureWorldSettings(), seed: 3);
    private readonly SimulationScheduler _scheduler;
    private readonly Entity _avatar;
    private readonly Entity _creature;

    public PactAnswerTests()
    {
        _scheduler = new SimulationScheduler(_world.Clock, _world.Events);
        _world.CreateSystems().RegisterAll(_scheduler);
        _avatar = _world.SpawnPlayerAvatar(Alice, Player);
        _creature = _world.SpawnCreature(_world.SpeciesIndex("mossback"), Player + new Vector3(0, 0, 9), new Personality(0, 0, 0, 0), group: 1);
    }

    public void Dispose() => _world.Dispose();

    [Fact]
    public void An_accepting_creature_considers_the_offer_then_walks_up_to_the_player_showing_affection()
    {
        Begin(accepted: true);

        Run((int)PactAnswerSystem.ConsiderTicks - 2);
        Assert.Equal(Emote.Curiosity, Shown());
        Assert.True(Facing().Z < -0.99f, "it faces the player while it thinks");
        Assert.InRange(Distance(), 8.9f, 9.1f);

        Run(60);
        Assert.Equal(Emote.Affection, Shown());
        Assert.True(Distance() < 6f, $"it walks toward the player (now {Distance():F1} m)");

        Run((int)PactAnswerSystem.TotalTicks);
        Assert.False(_world.World.Has<PactAnswer>(_creature), "then its own mind takes over again");
        Assert.True(Distance() < 4.5f, $"and it ended close to the player ({Distance():F1} m)");
    }

    [Fact]
    public void A_refusing_creature_turns_its_back_and_walks_away()
    {
        Begin(accepted: false);

        Run((int)PactAnswerSystem.TotalTicks - 2);

        Assert.True(Distance() > 9 + (PactAnswerSystem.TurnAwayMetres * 0.7f), $"it walked away ({Distance():F1} m)");
        Assert.True(Facing().Z > 0.9f, "with its back to the player");
        Assert.NotEqual(Emote.Affection, Shown());
    }

    [Fact]
    public void A_snared_creature_answers_on_the_spot()
    {
        _world.Snare(_creature, 0);
        Begin(accepted: false);

        Run((int)PactAnswerSystem.TotalTicks / 2);

        Assert.InRange(Distance(), 8.9f, 9.1f);
        Assert.True(Facing().Z > 0.99f, "it turns its back as far as the snare lets it");
    }

    [Fact]
    public void An_offer_starts_the_answer_that_matches_the_outcome()
    {
        var transaction = new OfferPactTransaction(_world, new CooldownTracker(), _avatar, _world.World.GetComponent<StableId>(_creature), PactTerms.Of(0));
        IntentResult result = new TransactionProcessor(new IntentDeduplicator()).Execute(new IntentId(1, 1), ref transaction, now: 0);

        PactAnswer answer = _world.World.GetComponent<PactAnswer>(_creature);
        Assert.Equal((BondingOutcome)result.Detail == BondingOutcome.Accepted ? 1 : 0, answer.Accepted);
        Assert.Equal(_avatar, answer.Player);
    }

    private void Begin(bool accepted) => PactAnswerSystem.Begin(_world.World, _creature, _avatar, accepted, _world.Clock.Current.Index);

    private void Run(int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            _scheduler.RunTick();
        }
    }

    private Emote Shown() => _world.World.GetComponent<CurrentEmote>(_creature).Value;

    private Vector3 Facing() => _world.World.GetComponent<Facing>(_creature).Value;

    private float Distance()
    {
        Vector3 offset = _world.World.GetComponent<Position>(_creature).Value - Player;
        return new Vector2(offset.X, offset.Z).Length();
    }
}
