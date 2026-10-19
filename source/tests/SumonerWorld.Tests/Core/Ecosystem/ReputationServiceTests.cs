using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Cognition.Opinions;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecosystem;
using SumonerWorld.Core.Jobs;
using SumonerWorld.Tests.Core.Bonding;
using static SumonerWorld.Tests.Core.Cognition.CognitionTestData;

namespace SumonerWorld.Tests.Core.Ecosystem;

public class ReputationServiceTests
{
    private static readonly ReputationKey HunterInMarsh = new(Species: 0, Region: 3, Player: 1001);

    [Fact]
    public void Events_change_the_score_only_after_the_aggregation_job_is_applied()
    {
        using var jobs = new JobScheduler(workerCount: 1, capacity: 8);
        var reputation = new ReputationService(ShippedRules(), jobs, pendingCapacity: 16, tableCapacity: 16);

        reputation.Record(BondingTestKit.Event("KilledKin"), HunterInMarsh);
        reputation.Record(BondingTestKit.Event("HuntedMyKind"), HunterInMarsh);
        reputation.Flush();
        Assert.Equal(0, reputation.GetScore(HunterInMarsh));

        jobs.ApplyCompleted();

        Assert.Equal(-300, reputation.GetScore(HunterInMarsh));
        Assert.Equal(1, reputation.Aggregations);
    }

    [Fact]
    public void Events_without_a_reputation_effect_are_ignored_and_scores_are_clamped()
    {
        using var jobs = new JobScheduler(workerCount: 0, capacity: 8);
        var reputation = new ReputationService(ShippedRules(), jobs, pendingCapacity: 64, tableCapacity: 16);

        reputation.Record(FedBy, HunterInMarsh);
        for (int i = 0; i < 20; i++)
        {
            reputation.Record(BondingTestKit.Event("KilledKin"), HunterInMarsh);
        }

        reputation.Flush();
        jobs.ApplyCompleted();

        Assert.Equal(-ReputationService.MaxScore, reputation.GetScore(HunterInMarsh));
    }

    [Fact]
    public void A_full_pending_buffer_is_summed_on_the_spot_never_dropped()
    {
        using var jobs = new JobScheduler(workerCount: 0, capacity: 8);
        var reputation = new ReputationService(ShippedRules(), jobs, pendingCapacity: 2, tableCapacity: 16);

        for (int i = 0; i < 5; i++)
        {
            reputation.Record(BondingTestKit.Event("SavedLifeOf"), HunterInMarsh);
        }

        reputation.Flush();
        jobs.ApplyCompleted();

        Assert.Equal(ReputationService.MaxScore, reputation.GetScore(HunterInMarsh));
    }

    [Fact]
    public void Scores_are_kept_per_species_region_and_player()
    {
        using var jobs = new JobScheduler(workerCount: 0, capacity: 8);
        var reputation = new ReputationService(ShippedRules(), jobs, pendingCapacity: 16, tableCapacity: 16);
        reputation.Record(BondingTestKit.Event("ProtectedYoung"), HunterInMarsh);
        reputation.Record(BondingTestKit.Event("KilledKin"), HunterInMarsh with { Region = 4 });
        reputation.Record(BondingTestKit.Event("KilledKin"), HunterInMarsh with { Player = 7 });
        reputation.Flush();
        jobs.ApplyCompleted();

        var known = new PlayerReputation[8];
        int count = reputation.KnownPlayers(species: 0, region: 3, known);

        Assert.Equal([new PlayerReputation(7, -200), new PlayerReputation(1001, 150)], known[..count]);
    }

    [Fact]
    public void A_new_wild_creature_inherits_its_kinds_opinion_of_known_players()
    {
        using var jobs = new JobScheduler(workerCount: 0, capacity: 8);
        var reputation = new ReputationService(ShippedRules(), jobs, pendingCapacity: 16, tableCapacity: 16);
        reputation.Record(BondingTestKit.Event("KilledKin"), HunterInMarsh);
        reputation.Record(BondingTestKit.Event("KilledKin"), HunterInMarsh);
        reputation.Flush();
        jobs.ApplyCompleted();
        var store = new CreatureMindStore(ShippedRules(), capacity: 2);
        MindHandle newborn = store.Allocate(new Personality(0, 0, 0, 0), Needs);

        int seeded = reputation.SeedOpinions(store, newborn, species: 0, region: 3);
        Opinion opinion = new OpinionModel(store, ShippedRules()).GetOpinion(newborn, new MemorySubject(SubjectKind.Player, 1001), 0);

        Assert.Equal(1, seeded);
        Assert.Equal(-20, opinion.Trust);
        Assert.Equal(20, opinion.Fear);
        Assert.True(opinion.Affection < 0);
    }
}
