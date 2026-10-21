using System.Numerics;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Cognition.Behaviour;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.World;
using SumonerWorld.Scenarios;

namespace SumonerWorld.Tests.Core.Combat;

[Collection(AllocationMeasurements.Name)]
public class CreatureCombatTests
{
    private const ulong Alice = 1001;
    private static readonly Vector3 Player = new(100, 0, 100);

    [Theory]
    [InlineData(1.2f, "bite")]
    [InlineData(3.5f, "pounce")]
    [InlineData(10f, null)]
    public void An_attack_intent_starts_the_first_move_that_reaches_the_focus(float distance, string? expected)
    {
        using var world = new CreatureWorld(ShippedData.Current, new CreatureWorldSettings(), seed: 5);
        var scheduler = new SimulationScheduler(world.Clock, world.Events);
        var attacks = new CreatureAttackSystem(world);
        scheduler.Register(attacks);
        Entity avatar = world.SpawnPlayerAvatar(Alice, Player);
        Entity fox = world.SpawnCreature(world.SpeciesIndex("bogfox"), Player + new Vector3(0, 0, distance), new Personality(80, 0, 0, 80), group: 1);
        world.World.GetComponent<FocusRef>(fox) = new FocusRef(avatar, 0, 1);

        world.Events.Publish(new CreatureIntent(fox, IntentKind.Attack, MoveTargetMode.Focus, 0, Emote.None, InteractVerb.Eat, null));
        scheduler.RunTick();

        if (expected is null)
        {
            Assert.False(world.World.Has<ActiveMove>(fox));
            return;
        }

        Assert.Equal(world.Data.MoveIndex(expected), world.World.GetComponent<ActiveMove>(fox).Move);
        Assert.True(world.World.GetComponent<Facing>(fox).Value.Z < -0.99f, "it turns to face the player");
    }

    [Fact]
    public void A_snared_creature_or_one_facing_a_downed_player_does_not_attack()
    {
        using var world = new CreatureWorld(ShippedData.Current, new CreatureWorldSettings(), seed: 5);
        var scheduler = new SimulationScheduler(world.Clock, world.Events);
        scheduler.Register(new CreatureAttackSystem(world));
        Entity avatar = world.SpawnPlayerAvatar(Alice, Player);
        Entity snared = world.SpawnCreature(world.SpeciesIndex("bogfox"), Player + new Vector3(0, 0, 1.2f), new Personality(80, 0, 0, 80), group: 1);
        Entity free = world.SpawnCreature(world.SpeciesIndex("bogfox"), Player + new Vector3(1.2f, 0, 0), new Personality(80, 0, 0, 80), group: 1);
        world.Snare(snared, 0);
        world.World.GetComponent<FocusRef>(snared) = new FocusRef(avatar, 0, 1);
        world.World.GetComponent<FocusRef>(free) = new FocusRef(avatar, 0, 1);
        world.World.GetComponent<Combatant>(avatar) = world.World.GetComponent<Combatant>(avatar) with { Health = 0 };

        world.Events.Publish(new CreatureIntent(snared, IntentKind.Attack, MoveTargetMode.Focus, 0, Emote.None, InteractVerb.Eat, null));
        world.Events.Publish(new CreatureIntent(free, IntentKind.Attack, MoveTargetMode.Focus, 0, Emote.None, InteractVerb.Eat, null));
        scheduler.RunTick();

        Assert.False(world.World.Has<ActiveMove>(snared));
        Assert.False(world.World.Has<ActiveMove>(free));
    }

    [Fact]
    public void A_bold_creature_with_a_grudge_fights_the_player_with_the_full_ai()
    {
        using var world = new CreatureWorld(ShippedData.Current, new CreatureWorldSettings(), seed: 9);
        var scheduler = new SimulationScheduler(world.Clock, world.Events);
        CreatureSystems systems = world.CreateSystems();
        systems.RegisterAll(scheduler);
        Entity avatar = world.SpawnPlayerAvatar(Alice, Player);
        Entity fox = world.SpawnCreature(world.SpeciesIndex("bogfox"), Player + new Vector3(0, 0, 4), new Personality(90, -50, 0, 90), group: 1);
        MindHandle mind = world.World.GetComponent<MindRef>(fox).Handle;
        for (int i = 0; i < 3; i++)
        {
            world.Minds.Remember(mind, world.Rules.FindByKey("AttackedBy")!.Value, new MemorySubject(SubjectKind.Player, Alice), 0);
        }

        for (int tick = 0; tick < 900 && world.World.GetComponent<Combatant>(avatar).Health == world.World.GetComponent<Combatant>(avatar).MaxHealth; tick++)
        {
            scheduler.RunTick();
        }

        Assert.True(systems.Attacks.Started > 0, "the fox started an attack");
        Assert.True(world.World.GetComponent<Combatant>(avatar).Health < world.World.GetComponent<Combatant>(avatar).MaxHealth, "and it landed");
    }

    [Fact]
    public void A_hurt_creature_chooses_to_flee_more_often_than_a_healthy_one()
    {
        Assert.True(TicksFleeing(healthShare: 0.1f) > TicksFleeing(healthShare: 1f));
    }

    private static int TicksFleeing(float healthShare)
    {
        using var world = new CreatureWorld(ShippedData.Current, new CreatureWorldSettings(), seed: 9);
        var scheduler = new SimulationScheduler(world.Clock, world.Events);
        world.CreateSystems().RegisterAll(scheduler);
        world.SpawnPlayerAvatar(Alice, Player);
        Entity fox = world.SpawnCreature(world.SpeciesIndex("bogfox"), Player + new Vector3(0, 0, 6), new Personality(0, 0, 0, 0), group: 1);
        world.Minds.Remember(world.World.GetComponent<MindRef>(fox).Handle, world.Rules.FindByKey("AttackedBy")!.Value, new MemorySubject(SubjectKind.Player, Alice), 0);
        ref Combatant combatant = ref world.World.GetComponent<Combatant>(fox);
        combatant.Health = (int)(combatant.MaxHealth * healthShare);
        combatant.LastHurtTick = 1_000_000; // no regeneration during the test
        ushort flee = world.Data.Goals.First(goal => goal.Key == "Flee").Id;

        int fleeing = 0;
        for (int tick = 0; tick < 300; tick++)
        {
            scheduler.RunTick();
            fleeing += world.World.GetComponent<CurrentGoal>(fox).Goal == flee ? 1 : 0;
        }

        return fleeing;
    }
}
