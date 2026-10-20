using System.Globalization;
using System.Numerics;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Cognition.Lod;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Cognition.Opinions;
using SumonerWorld.Core.Data;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.World;
using SumonerWorld.Net.Client;
using SumonerWorld.Net.Messages;
using SumonerWorld.Scenarios;

namespace SumonerWorld.Tests.Net;

public class DevReadoutTests
{
    private static readonly DataRegistry Data = ShippedData.Current;

    [Fact]
    public void The_inspector_names_everything_from_the_game_data_and_marks_the_chosen_goal()
    {
        GoalDefinition approach = Data.Goals.First(goal => goal.Key == "Approach");
        GoalDefinition flee = Data.Goals.First(goal => goal.Key == "Flee");
        BehaviourTreeDefinition tree = Data.BehaviourTrees.First(candidate => candidate.Goal == "Approach");
        int lastNode = CountNodes(tree.Root) - 1;
        ushort fedBy = Data.MemoryEventTypes.First(type => type.Key == "FedBy").Id;
        var alice = new MemorySubject(SubjectKind.Player, 1001);
        var inspection = new CreatureInspection(
            900, new StableId(42), 0, 3, AiLodTier.Active, new Vector3(250.4f, 12, 260.6f), Emote.Curiosity, approach.Id, alice, 120,
            new Personality(10, 0, 0, 0), new Personality(25, 0, 0, 0), new EmotionState(5, 0, 0, 56, 0), [62, 0, 0, 0, 0], 1, 2,
            [new InspectedMemory(fedBy, alice, 40, 812, 3, MemoryTags.None, 1_800)],
            [new InspectedOpinion(alice, new Opinion(30, -5, 12, 0))],
            [new GoalScore(flee.Id, 20), new GoalScore(approach.Id, 325)],
            approach.Id,
            lastNode);

        string text = DevReadout.Inspector(inspection, Data);

        Assert.Contains($"{Data.Species[0].Id} #42", text, StringComparison.Ordinal);
        Assert.Contains("goal  Approach", text, StringComparison.Ordinal);
        Assert.Contains("FedBy", text, StringComparison.Ordinal);
        Assert.Contains("player 1001", text, StringComparison.Ordinal);
        Assert.Contains("> Approach", text, StringComparison.Ordinal);
        Assert.Contains("  Flee", text, StringComparison.Ordinal);
        Assert.True(text.IndexOf("Approach  ", StringComparison.Ordinal) < text.IndexOf("Flee  ", StringComparison.Ordinal), "Goals are sorted by score.");
        Assert.Contains("drifted", text, StringComparison.Ordinal);
        Assert.Contains("60s ago", text, StringComparison.Ordinal);
        Assert.Equal($"Approach: {string.Join(" > ", tree.PathTo(lastNode).Select(node => node.Describe()))}", DevReadout.TreePath(inspection, Data));
        Assert.Equal("(not running)", DevReadout.TreePath(inspection with { TreeNode = -1 }, Data));
    }

    [Fact]
    public void The_overlay_marks_what_is_over_budget_and_reads_the_same_in_every_culture()
    {
        CultureInfo before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("et-EE");
            StageTiming[] stages = new StageTiming[ServerMetrics.StageCount];
            stages[(int)SimulationStage.Perception] = new StageTiming(1_500, 5_200);
            var metrics = new ServerMetrics(900, 15, new StageTiming(2_250, 40_000), stages, 1_094, [2, 2, 2], [4, 30, 166, 0], 1, 17_600, 17_600, 162);

            string overlay = DevReadout.Overlay(new ClientFrameStats(60, 16.7, 16.9, 60, 848, 3.2), metrics);

            Assert.Contains("tick  2.25 ms (max 40.00) of 33.3  !", overlay, StringComparison.Ordinal);
            Assert.Contains("AI    1.50 ms (max 5.20) of 4.0  !", overlay, StringComparison.Ordinal);
            Assert.Contains("Perception", overlay, StringComparison.Ordinal);
            Assert.DoesNotContain("Combat", overlay, StringComparison.Ordinal);
            Assert.Contains("Active 4  Near 30  Far 166", overlay, StringComparison.Ordinal);
            Assert.Contains("17.2 KiB/s", overlay, StringComparison.Ordinal);
            Assert.DoesNotContain(",", overlay.Replace(", ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
            Assert.Contains("no dev metrics", DevReadout.Overlay(default, null), StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    private static int CountNodes(BehaviourNodeDefinition node) => 1 + node.Nodes.Sum(CountNodes);
}
