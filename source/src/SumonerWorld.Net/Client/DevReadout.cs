using System.Globalization;
using System.Text;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Data;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.World;
using SumonerWorld.Net.Messages;

namespace SumonerWorld.Net.Client;

/// <summary>What the client measures itself for the dev overlay, over the last half second.</summary>
/// <param name="FramesPerSecond">Frames per second.</param>
/// <param name="FrameMeanMs">Mean frame time.</param>
/// <param name="FrameMaxMs">Longest frame.</param>
/// <param name="EntitiesShown">Remote creatures and players drawn.</param>
/// <param name="ServerTick">Newest snapshot's tick.</param>
/// <param name="RenderLagTicks">How far behind the newest snapshot remote entities are drawn.</param>
public readonly record struct ClientFrameStats(double FramesPerSecond, double FrameMeanMs, double FrameMaxMs, int EntitiesShown, ulong ServerTick, double RenderLagTicks);

/// <summary>
/// Text for the dev overlay and the AI Inspector panel (M1-22). Plain text in a fixed-width layout, names from the game data
/// (species, goals, memory event types, behaviour-tree nodes); values over budget are marked with <c>!</c>.
/// </summary>
public static class DevReadout
{
    /// <summary>Server tick budget (30 Hz).</summary>
    public const double TickBudgetMs = 1000.0 / SimulationSettings.DefaultTicksPerSecond;

    /// <summary>AI budget per tick (TDD §1, AI Bible §9).</summary>
    public const double AiBudgetMs = 4;

    private const double MicrosPerMs = 1000;
    private const double BytesPerKiB = 1024;
    private const int BarWidth = 10;
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>The overlay: client frame stats, then the server's tick, stages, AI, memory and bandwidth (when it sends them).</summary>
    public static string Overlay(ClientFrameStats client, ServerMetrics? server)
    {
        var text = new StringBuilder();
        Line(text, $"CLIENT  {client.FramesPerSecond,5:F0} fps  frame {client.FrameMeanMs,5:F1} ms (max {client.FrameMaxMs:F1}){Over(client.FrameMaxMs > 1000.0 / 30)}");
        Line(text, $"        {client.EntitiesShown} shown, server tick {client.ServerTick}, drawn {client.RenderLagTicks:F1} ticks behind");
        if (server is not ServerMetrics metrics)
        {
            Line(text, $"SERVER  no dev metrics (server without --dev-tools?)");
            return text.ToString();
        }

        double tickMean = metrics.TickTime.MeanMicros / MicrosPerMs;
        double tickMax = metrics.TickTime.MaxMicros / MicrosPerMs;
        Line(text, $"SERVER  tick {tickMean,5:F2} ms (max {tickMax:F2}) of {TickBudgetMs:F1}{Over(tickMax > TickBudgetMs)}   tick {metrics.Tick}, {metrics.Players} players");
        double aiMean = metrics.Ai.MeanMicros / MicrosPerMs;
        double aiMax = metrics.Ai.MaxMicros / MicrosPerMs;
        Line(text, $"        AI   {aiMean,5:F2} ms (max {aiMax:F2}) of {AiBudgetMs:F1}{Over(aiMax > AiBudgetMs)}");
        for (int i = 0; i < metrics.Stages.Count; i++)
        {
            StageTiming stage = metrics.Stages[i];
            if (stage.MaxMicros > 0)
            {
                Line(text, $"        {(SimulationStage)i,-16} {stage.MeanMicros / MicrosPerMs,6:F3} ms (max {stage.MaxMicros / MicrosPerMs:F3})");
            }
        }

        Line(text, $"        alloc {metrics.AllocatedPerTick} B/tick  GC {string.Join('/', metrics.Collections)}  {metrics.WorkingSetMiB} MiB");
        Line(text, $"        tiers {string.Join("  ", metrics.Tiers.Select((count, tier) => $"{(Core.Cognition.Lod.AiLodTier)tier} {count}"))}");
        Line(text, $"        net to you {metrics.ClientBytesPerSecond / BytesPerKiB:F1} KiB/s, all clients {metrics.SentBytesPerSecond / BytesPerKiB:F1} KiB/s");
        return text.ToString();
    }

    /// <summary>The AI Inspector panel for one creature.</summary>
    public static string Inspector(CreatureInspection inspection, IDataRegistry data)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        ArgumentNullException.ThrowIfNull(data);
        var text = new StringBuilder();
        Line(text, $"{SpeciesName(inspection.Species, data)} #{inspection.Id.Value}   group {inspection.Group}   {inspection.Tier}   tick {inspection.Tick}");
        Line(text, $"at {inspection.Position.X:F0}, {inspection.Position.Z:F0}   emote {inspection.Emote}");
        Line(text, $"goal  {GoalName(inspection.Goal, data)}");
        Line(text, $"tree  {TreePath(inspection, data)}");
        Line(text, $"focus {(inspection.Focus is MemorySubject focus ? $"{SubjectName(focus, data)} at {inspection.FocusDistance / 10.0:F0}%" : "none")}");

        Section(text, "TRAITS (now / birth)");
        Personality now = inspection.Personality;
        Personality birth = inspection.Birth;
        foreach (PersonalityTrait trait in Enum.GetValues<PersonalityTrait>())
        {
            Line(text, $"  {trait,-10} {now.Get(trait),4} / {birth.Get(trait),4}{(now.Get(trait) != birth.Get(trait) ? "  drifted" : string.Empty)}");
        }

        Section(text, "EMOTIONS / NEEDS");
        Line(text, $"  fear {inspection.Emotions.Fear,3}  anger {inspection.Emotions.Anger,3}  joy {inspection.Emotions.Joy,3}  curiosity {inspection.Emotions.Curiosity,3}  stress {inspection.Emotions.Stress,3}");
        for (int i = 0; i < inspection.Needs.Count; i++)
        {
            Line(text, $"  {(Need)i,-10} {Bar(inspection.Needs[i], ICreatureMindStore.MaxNeed)} {inspection.Needs[i],3}");
        }

        Section(text, $"MEMORIES ({inspection.CoreMemories} core, {inspection.EpisodicMemories} episodic; strongest first)");
        foreach (InspectedMemory memory in inspection.Memories)
        {
            string tags = memory.Tags == MemoryTags.None ? string.Empty : $" [{memory.Tags}]";
            Line(text, $"  {EventName(memory.EventType, data),-18} {SubjectName(memory.Subject, data),-14} {memory.Salience,4} {Signed(memory.Valence),5} x{memory.Count} {Age(memory.AgeTicks)}{tags}");
        }

        Section(text, "OPINIONS  trust fear affection respect");
        foreach (InspectedOpinion opinion in inspection.Opinions)
        {
            Line(text, $"  {SubjectName(opinion.Subject, data),-14} {Signed(opinion.Opinion.Trust),5} {Signed(opinion.Opinion.Fear),5} {Signed(opinion.Opinion.Affection),5} {Signed(opinion.Opinion.Respect),5}");
        }

        Section(text, "GOAL SCORES");
        int best = inspection.Goals.Count == 0 ? 1 : Math.Max(1, inspection.Goals.Max(goal => goal.Score));
        foreach (GoalScore goal in inspection.Goals.OrderByDescending(goal => goal.Score).ThenBy(goal => goal.Goal))
        {
            Line(text, $"{(goal.Goal == inspection.Goal ? "> " : "  ")}{GoalName(goal.Goal, data),-14} {Bar(goal.Score, best)} {goal.Score,6}");
        }

        return text.ToString();
    }

    /// <summary>The running node and its ancestors, e.g. <c>Flee: Sequence › MoveTo AwayFromFocus 150%</c>.</summary>
    public static string TreePath(CreatureInspection inspection, IDataRegistry data)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        ArgumentNullException.ThrowIfNull(data);
        if (inspection.TreeNode < 0)
        {
            return "(not running)";
        }

        string goal = GoalName(inspection.TreeGoal, data);
        BehaviourTreeDefinition? tree = data.BehaviourTrees.FirstOrDefault(candidate => candidate.Goal == goal);
        IReadOnlyList<BehaviourNodeDefinition> path = tree?.PathTo(inspection.TreeNode) ?? [];
        return path.Count == 0 ? $"{goal}: node {inspection.TreeNode}" : $"{goal}: {string.Join(" > ", path.Select(node => node.Describe()))}";
    }

    private static string SpeciesName(int index, IDataRegistry data) => index < data.Species.Count ? data.Species[index].Id : $"species {index}";

    private static string GoalName(ushort id, IDataRegistry data) => data.Goals.FirstOrDefault(goal => goal.Id == id)?.Key ?? (id == 0 ? "Idle" : $"goal {id}");

    private static string EventName(ushort id, IDataRegistry data) => data.MemoryEventTypes.FirstOrDefault(type => type.Id == id)?.Key ?? $"event {id}";

    private static string SubjectName(MemorySubject subject, IDataRegistry data) => subject.Kind switch
    {
        SubjectKind.Player => $"player {subject.Id}",
        SubjectKind.Creature => $"creature #{subject.Id}",
        SubjectKind.Species => subject.Id < (ulong)data.Species.Count ? data.Species[(int)subject.Id].Id : $"species {subject.Id}",
        SubjectKind.Place => $"place {subject.Id}",
        _ => $"item {subject.Id}",
    };

    private static string Age(ulong ticks)
    {
        double seconds = ticks / (double)SimulationSettings.DefaultTicksPerSecond;
        return seconds < 120 ? $"{seconds:F0}s ago" : seconds < 7_200 ? $"{seconds / 60:F0}m ago" : $"{seconds / 3_600:F1}h ago";
    }

    private static string Bar(int value, int max)
    {
        int filled = max <= 0 ? 0 : Math.Clamp((int)Math.Round(value * (double)BarWidth / max), 0, BarWidth);
        return new string('#', filled) + new string('.', BarWidth - filled);
    }

    private static string Signed(int value) => value.ToString("+0;-0;0", Invariant);

    private static string Over(bool over) => over ? "  !" : string.Empty;

    private static void Section(StringBuilder text, string title) => text.Append('\n').Append(title).Append('\n');

    // Invariant culture: the readout looks the same on every PC (no decimal commas).
    private static void Line(StringBuilder text, FormattableString line) => text.Append(line.ToString(Invariant)).Append('\n');
}
