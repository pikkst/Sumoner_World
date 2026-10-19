using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Cognition.Decision;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Cognition.Opinions;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Randomness;
using static SumonerWorld.Tests.Core.Cognition.CognitionTestData;

namespace SumonerWorld.Tests.Core.Cognition;

public class UtilityEvaluatorTests
{
    private const int FullHealth = 1000;
    private const int Near = 200;

    private static readonly Personality Average = new(0, 0, 0, 0);
    private static readonly Personality Bold = new(Boldness: 80, Loyalty: 0, Curiosity: 0, Temper: 0);
    private static readonly Personality Timid = new(Boldness: -80, Loyalty: 0, Curiosity: 0, Temper: 0);
    private static readonly Personality Curious = new(Boldness: 20, Loyalty: 0, Curiosity: 90, Temper: 0);

    [Fact]
    public void A_hungry_creature_eats()
    {
        var ai = new Ai(Average);
        ulong now = 2UL * TicksPerDay;

        Assert.Equal(Goal("Eat"), ai.Decide(now).Goal);
    }

    [Fact]
    public void A_terrified_creature_flees()
    {
        var ai = new Ai(Average);
        ai.Appraisal.Stimulate(ai.Mind, Emotion.Fear, 90, 10);

        Assert.Equal(Goal("Flee"), ai.Decide(10).Goal);
    }

    [Fact]
    public void Timid_creatures_flee_sooner_than_bold_ones()
    {
        var timid = new Ai(Timid);
        var bold = new Ai(Bold);
        timid.Appraisal.Stimulate(timid.Mind, Emotion.Fear, 50, 10);
        bold.Appraisal.Stimulate(bold.Mind, Emotion.Fear, 50, 10);

        int timidFlee = timid.Score("Flee", 10);
        int boldFlee = bold.Score("Flee", 10);

        Assert.True(timidFlee > boldFlee * 3, $"Timid flee {timidFlee} vs bold {boldFlee}.");
    }

    [Fact]
    public void A_curious_creature_approaches_a_stranger_but_avoids_one_that_attacked_it()
    {
        var ai = new Ai(Curious);
        ai.Appraisal.Appraise(ai.Mind, Alice, 10);

        Assert.Equal(Goal("Approach"), ai.Decide(10, focus: Alice).Goal);

        ai.Store.Remember(ai.Mind, AttackedBy, Alice, 20);
        ai.Appraisal.Appraise(ai.Mind, Alice, 20);
        ushort afterAttack = ai.Decide(20, focus: Alice).Goal;

        Assert.True(afterAttack == Goal("Avoid") || afterAttack == Goal("Flee"), $"After an attack it should avoid or flee, chose {afterAttack}.");
    }

    [Fact]
    public void The_current_goal_gets_a_ten_percent_momentum_bonus()
    {
        var ai = new Ai(Average);
        ulong now = TicksPerDay;

        int fresh = ai.Score("Eat", now);
        int continuing = ai.Score("Eat", now, current: Goal("Eat"));

        Assert.Equal(fresh + (fresh / 10), continuing);
    }

    [Fact]
    public void Exact_ties_go_to_the_lower_goal_id_and_near_ties_are_decided_by_the_creatures_own_stream()
    {
        GoalDefinition Constant(ushort id, string key, int weight) => new()
        {
            Id = id,
            Key = key,
            Weight = weight,
            Interrupt = false,
            Personality = [],
            Considerations = [new ConsiderationDefinition { Input = ConsiderationInput.Constant, Curve = new CurveDefinition { Type = CurveType.Linear, Slope = 1000 } }],
        };

        var tie = new Ai(Average, [Constant(30, "Alpha", 500), Constant(20, "Beta", 500)]);
        Assert.Equal(20, tie.Decide(1).Goal);

        var near = new Ai(Average, [Constant(30, "Alpha", 500), Constant(20, "Beta", 490)]);
        var picks = new HashSet<ushort>();
        for (ulong seed = 0; seed < 50; seed++)
        {
            ushort first = near.Decide(1, random: new SeededRandom(seed)).Goal;
            Assert.Equal(first, near.Decide(1, random: new SeededRandom(seed)).Goal);
            picks.Add(first);
        }

        Assert.Equal(new ushort[] { 20, 30 }, picks.Order());

        var clear = new Ai(Average, [Constant(30, "Alpha", 500), Constant(20, "Beta", 400)]);
        Assert.Equal(30, clear.Decide(1).Goal);
    }

    [Fact]
    public void A_sudden_scare_interrupts_eating_but_a_calm_creature_keeps_eating()
    {
        var ai = new Ai(Average);
        ulong now = TicksPerDay;
        ushort eat = Goal("Eat");

        Assert.Equal(Decision.Idle, ai.CheckInterrupt(now, eat).Goal);

        ai.Appraisal.Stimulate(ai.Mind, Emotion.Fear, 95, now);
        Assert.Equal(Goal("Flee"), ai.CheckInterrupt(now, eat).Goal);
    }

    [Fact]
    public void Unknown_goals_fail_loudly()
    {
        var ai = new Ai(Average);
        SpeciesDefinition species = Registry.GetSpecies("mossback") with { Goals = ["Eat", "Teleport"] };

        Assert.Throws<ArgumentException>(() => ai.Evaluator.ForSpecies(species));
        Assert.Throws<ArgumentOutOfRangeException>(() => ai.Score("Hunt", 1));
    }

    [Fact]
    public void Deciding_does_not_allocate()
    {
        var ai = new Ai(Curious);
        var random = new SeededRandom(1);
        ai.Store.Remember(ai.Mind, FedBy, Alice, 0);
        ai.Decide(1, focus: Alice, random: random);
        ushort eat = Goal("Eat");

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (ulong tick = 2; tick < 3_000; tick++)
        {
            ai.Decide(tick, focus: Alice, random: random);
            ai.CheckInterrupt(tick, eat);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static ushort Goal(string key) => Registry.Goals.Single(goal => goal.Key == key).Id;

    private sealed class Ai
    {
        private readonly GoalSet _goals;

        public Ai(Personality personality, IEnumerable<GoalDefinition>? goals = null)
        {
            MemoryRules rules = ShippedRules();
            (Store, Mind) = (new CreatureMindStore(rules, capacity: 2), default);
            Mind = Store.Allocate(personality, Needs);
            var opinions = new OpinionModel(Store, rules);
            Appraisal = new AppraisalModel(Store, opinions, rules, AppraisalSettings.Default);
            GoalDefinition[] all = [.. goals ?? Registry.Goals];
            Evaluator = new UtilityEvaluator(Store, Appraisal, opinions, all, TicksPerDay);
            SpeciesDefinition species = Registry.GetSpecies("bogfox");
            _goals = Evaluator.ForSpecies(goals is null
                ? species with { Goals = ["Flee", "Eat", "Rest", "Socialize", "Explore", "Approach", "Avoid"] }
                : species with { Goals = [.. all.Select(goal => goal.Key)] });
        }

        public CreatureMindStore Store { get; }

        public MindHandle Mind { get; }

        public AppraisalModel Appraisal { get; }

        public UtilityEvaluator Evaluator { get; }

        public Decision Decide(ulong now, MemorySubject? focus = null, IRandom? random = null) =>
            Evaluator.Decide(Mind, Context(now, Decision.Idle, focus), random ?? new SeededRandom(0));

        public Decision CheckInterrupt(ulong now, ushort current) => Evaluator.CheckInterrupt(Mind, Context(now, current, null));

        public int Score(string goal, ulong now, ushort current = Decision.Idle) =>
            Evaluator.Score(Mind, UtilityEvaluatorTests.Goal(goal), Context(now, current, null));

        private DecisionContext Context(ulong now, ushort current, MemorySubject? focus) => new(_goals, now, current, focus, Near, FullHealth);
    }
}
