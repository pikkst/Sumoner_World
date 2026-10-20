using System.Numerics;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Cognition.Decision;
using SumonerWorld.Core.Cognition.Lod;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Cognition.Opinions;
using SumonerWorld.Core.Ecs;

namespace SumonerWorld.Core.World;

/// <summary>One memory as the AI Inspector shows it.</summary>
/// <param name="EventType">Memory event type id (data).</param>
/// <param name="Subject">Who or what it is about.</param>
/// <param name="Valence">Good (+) or bad (−), −100 … 100.</param>
/// <param name="Salience">Strength now, after decay, 0 … 1000.</param>
/// <param name="Count">Episodes merged into it.</param>
/// <param name="Tags">Core, rumour, witnessed, pinned.</param>
/// <param name="AgeTicks">Ticks since it first formed.</param>
public readonly record struct InspectedMemory(ushort EventType, MemorySubject Subject, sbyte Valence, int Salience, int Count, MemoryTags Tags, ulong AgeTicks);

/// <summary>What a creature thinks of one subject now (memories plus baseline).</summary>
/// <param name="Subject">The subject.</param>
/// <param name="Opinion">Trust, Fear, Affection, Respect.</param>
public readonly record struct InspectedOpinion(MemorySubject Subject, Opinion Opinion);

/// <summary>One goal's utility score now (momentum included for the current goal).</summary>
/// <param name="Goal">Goal id.</param>
/// <param name="Score">Score.</param>
public readonly record struct GoalScore(ushort Goal, int Score);

/// <summary>
/// Everything the AI Inspector shows about one creature at one tick (AI Bible §13): traits, needs, emotions, top memories,
/// opinions, goal scores and the running behaviour-tree node. Dev tools only.
/// </summary>
/// <param name="Tick">World tick it was taken at.</param>
/// <param name="Id">The creature.</param>
/// <param name="Species">Species index.</param>
/// <param name="Group">Herd or pack.</param>
/// <param name="Tier">AI level of detail.</param>
/// <param name="Position">Where it is.</param>
/// <param name="Emote">Emote shown.</param>
/// <param name="Goal">Goal the utility AI chose.</param>
/// <param name="Focus">What it pays attention to, if anything.</param>
/// <param name="FocusDistance">Normalised distance to the focus, 0 … 1000.</param>
/// <param name="Birth">Personality at birth.</param>
/// <param name="Personality">Personality now (after drift from core memories).</param>
/// <param name="Emotions">Emotions now.</param>
/// <param name="Needs">Needs now, 0 … 100, indexed by <see cref="Need"/>.</param>
/// <param name="CoreMemories">Records in core slots.</param>
/// <param name="EpisodicMemories">Episodic records.</param>
/// <param name="Memories">Strongest memories first.</param>
/// <param name="Opinions">Opinion of the focus first, then of the subjects of the strongest memories.</param>
/// <param name="Goals">Every goal of the species with its score, in goal-id order.</param>
/// <param name="TreeGoal">Goal whose tree ran at the last behaviour tick.</param>
/// <param name="TreeNode">Innermost running node of that tree (pre-order index), −1 when none.</param>
public sealed record CreatureInspection(
    ulong Tick,
    StableId Id,
    ushort Species,
    int Group,
    AiLodTier Tier,
    Vector3 Position,
    Emote Emote,
    ushort Goal,
    MemorySubject? Focus,
    int FocusDistance,
    Personality Birth,
    Personality Personality,
    EmotionState Emotions,
    IReadOnlyList<int> Needs,
    int CoreMemories,
    int EpisodicMemories,
    IReadOnlyList<InspectedMemory> Memories,
    IReadOnlyList<InspectedOpinion> Opinions,
    IReadOnlyList<GoalScore> Goals,
    ushort TreeGoal,
    int TreeNode)
{
    /// <summary>Most memories listed.</summary>
    public const int MaxMemories = 8;

    /// <summary>Most opinions listed.</summary>
    public const int MaxOpinions = 4;

    /// <summary>Most goals listed.</summary>
    public const int MaxGoals = 16;
}

/// <summary>
/// Builds <see cref="CreatureInspection"/>s from a <see cref="CreatureWorld"/> for the AI Inspector (dev tools, AI Bible §13).
/// <b>Read-only</b>: it uses only queries that change nothing (emotions are peeked, goals scored with a read-only sampler), so
/// inspecting never changes what the simulation does. Allocates; call it between ticks, never from a system.
/// </summary>
public sealed class CreatureInspector
{
    private const int FullHealth = 1000;
    private static readonly int NeedCount = Enum.GetValues<Need>().Length;

    private readonly CreatureWorld _world;
    private readonly GoalSet[] _goalsBySpecies;
    private readonly RecalledMemory[] _recalled = new RecalledMemory[CreatureInspection.MaxMemories];

    /// <summary>Creates the inspector for a world.</summary>
    public CreatureInspector(CreatureWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
        _goalsBySpecies = [.. world.Data.Species.Select(world.Services.Evaluator.ForSpecies)];
    }

    /// <summary>Inspects the live creature with this stable id; null when there is none.</summary>
    public CreatureInspection? Inspect(StableId id)
    {
        if (!_world.TryFindCreature(id, out Entity creature))
        {
            return null;
        }

        EcsWorld ecs = _world.World;
        ulong now = _world.Clock.Current.Index;
        MindHandle mind = ecs.GetComponent<MindRef>(creature).Handle;
        ushort species = ecs.GetComponent<SpeciesRef>(creature).Index;
        ushort goal = ecs.GetComponent<CurrentGoal>(creature).Goal;
        FocusRef focus = ecs.GetComponent<FocusRef>(creature);
        MemorySubject? focusSubject = _world.Services.Resolver.FocusSubject(focus);
        CreatureMindStore minds = _world.Minds;

        int[] needs = new int[NeedCount];
        for (int i = 0; i < needs.Length; i++)
        {
            needs[i] = minds.GetNeed(mind, (Need)i, now);
        }

        InspectedMemory[] memories = Memories(mind, now);
        var context = new DecisionContext(_goalsBySpecies[species], now, goal, focusSubject, focus.Distance, FullHealth);
        _world.Services.Runner.TryGetActiveNode(mind, out ushort treeGoal, out int treeNode);
        return new CreatureInspection(
            now,
            id,
            species,
            ecs.GetComponent<GroupRef>(creature).Group,
            ecs.GetComponent<AiTier>(creature).Tier,
            ecs.GetComponent<Position>(creature).Value,
            ecs.GetComponent<CurrentEmote>(creature).Value,
            goal,
            focusSubject,
            focus.Distance,
            minds.GetBirthPersonality(mind),
            minds.GetPersonality(mind),
            _world.Appraisal.PeekEmotions(mind, now),
            needs,
            minds.GetCoreRecords(mind).Length,
            minds.GetEpisodicRecords(mind).Length,
            memories,
            Opinions(mind, focusSubject, memories, now),
            Scores(mind, context),
            treeGoal,
            treeNode);
    }

    private InspectedMemory[] Memories(MindHandle mind, ulong now)
    {
        int recalled = _world.Minds.Recall(mind, now, _recalled);
        var memories = new InspectedMemory[recalled];
        for (int i = 0; i < recalled; i++)
        {
            MemoryRecord record = _recalled[i].Record;
            memories[i] = new InspectedMemory(
                record.EventType, record.Subject, record.Valence, _recalled[i].EffectiveSalience, record.Count, record.Tags, now - record.CreatedTick);
        }

        return memories;
    }

    private GoalScore[] Scores(MindHandle mind, in DecisionContext context)
    {
        IReadOnlyList<Data.Definitions.GoalDefinition> goals = context.Goals.Goals;
        var scores = new GoalScore[Math.Min(goals.Count, CreatureInspection.MaxGoals)];
        for (int i = 0; i < scores.Length; i++)
        {
            scores[i] = new GoalScore(goals[i].Id, _world.Services.Evaluator.Score(mind, goals[i].Id, context));
        }

        return scores;
    }

    private InspectedOpinion[] Opinions(MindHandle mind, MemorySubject? focus, InspectedMemory[] memories, ulong now)
    {
        var subjects = new List<MemorySubject>(CreatureInspection.MaxOpinions);
        if (focus is MemorySubject focused)
        {
            subjects.Add(focused);
        }

        foreach (InspectedMemory memory in memories)
        {
            if (subjects.Count < CreatureInspection.MaxOpinions && !subjects.Contains(memory.Subject))
            {
                subjects.Add(memory.Subject);
            }
        }

        return [.. subjects.Select(subject => new InspectedOpinion(subject, _world.Opinions.GetOpinion(mind, subject, now)))];
    }
}
