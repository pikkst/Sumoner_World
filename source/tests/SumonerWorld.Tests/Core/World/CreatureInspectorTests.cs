using System.Numerics;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Cognition.Decision;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.World;
using SumonerWorld.Scenarios;
using SumonerWorld.Scenarios.B5;

namespace SumonerWorld.Tests.Core.World;

public class CreatureInspectorTests
{
    [Fact]
    public void Inspecting_every_creature_every_tick_does_not_change_the_simulation()
    {
        using var watched = new B5Scenario(seed: 0xD0E5);
        using var alone = new B5Scenario(seed: 0xD0E5);
        var inspector = new CreatureInspector(watched.World);
        StableId[] ids = [.. watched.World.World.Store<StableId>().Components.ToArray()];
        int inspected = 0;
        for (int tick = 0; tick < 600; tick++)
        {
            watched.RunTick();
            alone.RunTick();
            foreach (StableId id in ids)
            {
                inspected += inspector.Inspect(id) is null ? 0 : 1;
            }
        }

        Assert.True(inspected > 100_000, $"{inspected} inspections");
        Assert.True(watched.PlayerFocusEvents > 0, "Creatures appraised the players, so emotions were in play.");
        Assert.Equal(alone.ComputeStateHash(), watched.ComputeStateHash());
    }

    [Fact]
    public void An_inspection_shows_the_mind_as_the_simulation_sees_it()
    {
        using var world = new CreatureWorld(ShippedData.Current, new CreatureWorldSettings(), seed: 21);
        ushort species = world.SpeciesIndex("mossback");
        Entity creature = world.SpawnCreature(species, new Vector3(100, 0, 100), new Personality(40, -10, 60, 5), group: 3);
        StableId id = world.World.GetComponent<StableId>(creature);
        MindHandle mind = world.World.GetComponent<MindRef>(creature).Handle;
        var alice = new MemorySubject(SubjectKind.Player, 1001);
        world.Minds.Remember(mind, world.Rules.FindByKey("FedBy")!.Value, alice, now: 0);
        world.Minds.Remember(mind, world.Rules.FindByKey("AttackedBy")!.Value, new MemorySubject(SubjectKind.Player, 1002), now: 0);
        world.Appraisal.Stimulate(mind, Emotion.Fear, 70, now: 0);

        CreatureInspection inspection = new CreatureInspector(world).Inspect(id)!;

        Assert.Equal((id, species, 3, new Vector3(100, 0, 100)), (inspection.Id, inspection.Species, inspection.Group, inspection.Position));
        Assert.Equal(new Personality(40, -10, 60, 5), inspection.Birth);
        Assert.Equal(70, inspection.Emotions.Fear);
        Assert.Equal(2, inspection.Memories.Count);
        Assert.Contains(inspection.Memories, memory => memory.Subject == alice && memory.Salience > 0);
        Assert.Equal(world.Opinions.GetOpinion(mind, alice, 0), inspection.Opinions.Single(opinion => opinion.Subject == alice).Opinion);
        Assert.Equal(Enum.GetValues<Need>().Length, inspection.Needs.Count);
        Assert.Equal(-1, inspection.TreeNode);
    }

    [Fact]
    public void An_inspection_lists_every_goal_with_the_score_the_utility_ai_gives_it()
    {
        using var world = new CreatureWorld(ShippedData.Current, new CreatureWorldSettings(), seed: 22);
        ushort species = world.SpeciesIndex("bogfox");
        Entity creature = world.SpawnCreature(species, new Vector3(50, 0, 50), new Personality(0, 0, 0, 0), group: 1);
        MindHandle mind = world.World.GetComponent<MindRef>(creature).Handle;

        CreatureInspection inspection = new CreatureInspector(world).Inspect(world.World.GetComponent<StableId>(creature))!;

        GoalSet goals = world.Services.Evaluator.ForSpecies(world.Data.Species[species]);
        Assert.Equal(goals.Goals.Select(goal => goal.Id), inspection.Goals.Select(goal => goal.Goal));
        var context = new DecisionContext(goals, 0, inspection.Goal, null, 0, 1000);
        Assert.Equal(goals.Goals.Select(goal => world.Services.Evaluator.Score(mind, goal.Id, context)), inspection.Goals.Select(goal => goal.Score));
        Assert.Null(new CreatureInspector(world).Inspect(new StableId(999_999)));
    }
}
