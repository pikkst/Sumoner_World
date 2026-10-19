using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Cognition.Opinions;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Tests.Core.Cognition;

namespace SumonerWorld.Tests.Core.Bonding;

/// <summary>Bonding objects built from the shipped data.</summary>
internal sealed class BondingTestKit
{
    public const int Day = CognitionTestData.TicksPerDay;
    public static readonly MemorySubject Alice = new(SubjectKind.Player, 1001);
    public static readonly MemorySubject Bob = new(SubjectKind.Player, 1002);

    public BondingTestKit(Personality? personality = null)
    {
        Rules = CognitionTestData.ShippedRules();
        Store = new CreatureMindStore(Rules, capacity: 4);
        Creature = Store.Allocate(personality ?? new Personality(0, 40, 0, 0), CognitionTestData.Needs);
        Opinions = new OpinionModel(Store, Rules);
        Evaluator = new PactEvaluator(Store, Opinions, CognitionTestData.Registry.PactTerms);
        Bonding = new BondingRules(Store, Opinions, Evaluator, Rules, CognitionTestData.Registry.BondLevels, CognitionTestData.Registry.PactTerms.Count, Day);
        Obedience = new ObedienceModel(Store, Opinions, ObedienceSettings.Default);
    }

    public MemoryRules Rules { get; }

    public CreatureMindStore Store { get; }

    public MindHandle Creature { get; }

    public OpinionModel Opinions { get; }

    public PactEvaluator Evaluator { get; }

    public BondingRules Bonding { get; }

    public ObedienceModel Obedience { get; }

    public static SpeciesDefinition Species(int threshold) => CognitionTestData.Registry.GetSpecies("mossback") with { PactThreshold = threshold };

    public static int Term(string key) =>
        CognitionTestData.Registry.PactTerms.Select((term, index) => (term, index)).Single(item => item.term.Key == key).index;

    public static ushort Event(string key) => CognitionTestData.Registry.MemoryEventTypes.Single(type => type.Key == key).Id;

    public BondingOutcome Bond(MemorySubject player, ulong now = 0) =>
        Bonding.OfferPact(Creature, Species(threshold: -1000), player, PactTerms.Of(Term("FeedDaily")), 0, now, out _);

    public int CountMemories(string key, MemorySubject about, ulong now)
    {
        var buffer = new RecalledMemory[64];
        int count = Store.RecallAbout(Creature, about, now, buffer);
        ushort id = Event(key);
        return buffer.Take(count).Where(memory => memory.Record.EventType == id).Sum(memory => (int)memory.Record.Count);
    }
}
