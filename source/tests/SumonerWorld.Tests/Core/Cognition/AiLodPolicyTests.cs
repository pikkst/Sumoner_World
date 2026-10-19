using SumonerWorld.Core.Cognition.Lod;

namespace SumonerWorld.Tests.Core.Cognition;

public class AiLodPolicyTests
{
    private readonly AiLodPolicy _policy = new(AiLodSettings.Default);

    [Theory]
    [InlineData(10f, AiLodTier.Active)]
    [InlineData(40f, AiLodTier.Active)]
    [InlineData(41f, AiLodTier.Near)]
    [InlineData(120f, AiLodTier.Near)]
    [InlineData(500f, AiLodTier.Far)]
    [InlineData(float.PositiveInfinity, AiLodTier.Far)]
    public void Distance_to_the_nearest_player_sets_the_tier(float distance, AiLodTier expected)
    {
        Assert.Equal(expected, _policy.Classify(AiLodTier.Far, distance, engaged: false, loaded: true));
    }

    [Fact]
    public void Combat_or_summoning_keeps_a_creature_active_and_unloaded_chunks_are_abstract()
    {
        Assert.Equal(AiLodTier.Active, _policy.Classify(AiLodTier.Far, 900f, engaged: true, loaded: true));
        Assert.Equal(AiLodTier.Abstract, _policy.Classify(AiLodTier.Active, 5f, engaged: true, loaded: false));
    }

    [Fact]
    public void Hysteresis_stops_tiers_flickering_at_a_boundary()
    {
        Assert.Equal(AiLodTier.Active, _policy.Classify(AiLodTier.Active, 45f, engaged: false, loaded: true));
        Assert.Equal(AiLodTier.Near, _policy.Classify(AiLodTier.Active, 51f, engaged: false, loaded: true));
        Assert.Equal(AiLodTier.Near, _policy.Classify(AiLodTier.Near, 125f, engaged: false, loaded: true));
        Assert.Equal(AiLodTier.Far, _policy.Classify(AiLodTier.Far, 125f, engaged: false, loaded: true));
    }

    [Theory]
    [InlineData(AiLodTier.Active, AiActivity.Behaviour, 30)]
    [InlineData(AiLodTier.Active, AiActivity.Perception, 10)]
    [InlineData(AiLodTier.Near, AiActivity.Decision, 2)]
    [InlineData(AiLodTier.Far, AiActivity.Perception, 1)]
    [InlineData(AiLodTier.Abstract, AiActivity.Behaviour, 0)]
    public void Each_creature_runs_at_its_tier_rate(AiLodTier tier, AiActivity activity, int timesPerSecond)
    {
        int runs = Enumerable.Range(0, 30).Count(tick => _policy.IsDue(tier, activity, creatureIndex: 7, (ulong)tick));

        Assert.Equal(timesPerSecond, runs);
    }

    [Fact]
    public void Work_is_spread_evenly_over_ticks()
    {
        const int creatures = 300;
        int[] perTick = new int[30];
        for (int tick = 0; tick < perTick.Length; tick++)
        {
            for (int creature = 0; creature < creatures; creature++)
            {
                perTick[tick] += _policy.IsDue(AiLodTier.Far, AiActivity.Perception, creature, (ulong)tick) ? 1 : 0;
            }
        }

        Assert.All(perTick, count => Assert.Equal(creatures / 30, count));
    }

    [Fact]
    public void Invalid_settings_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new AiLodPolicy(new AiLodSettings { ActiveRadius = 100, NearRadius = 50 }));
        Assert.Throws<ArgumentException>(() => new AiLodPolicy(new AiLodSettings { Intervals = [[1, 1, 1], [0, 1, 1], [1, 1, 1]] }));
    }
}
