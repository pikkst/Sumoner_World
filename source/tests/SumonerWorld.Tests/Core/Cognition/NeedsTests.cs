using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Cognition.Memory;
using static SumonerWorld.Tests.Core.Cognition.CognitionTestData;

namespace SumonerWorld.Tests.Core.Cognition;

public class NeedsTests
{
    private static readonly Personality Calm = new(0, 0, 0, 0);

    [Fact]
    public void Needs_start_at_zero_and_rise_at_the_species_rate_per_day()
    {
        var (store, mind) = StoreWithMind(Calm);

        Assert.Equal(0, store.GetNeed(mind, Need.Hunger, 0));
        Assert.Equal(30, store.GetNeed(mind, Need.Hunger, TicksPerDay / 2));
        Assert.Equal(60, store.GetNeed(mind, Need.Hunger, TicksPerDay));
        Assert.Equal(10, store.GetNeed(mind, Need.Territory, TicksPerDay));
    }

    [Fact]
    public void Needs_stop_at_100()
    {
        var (store, mind) = StoreWithMind(Calm);

        Assert.Equal(ICreatureMindStore.MaxNeed, store.GetNeed(mind, Need.Social, 30UL * TicksPerDay));
    }

    [Fact]
    public void Satisfying_a_need_lowers_it_and_it_rises_again_from_there()
    {
        var (store, mind) = StoreWithMind(Calm);

        store.SatisfyNeed(mind, Need.Hunger, 40, TicksPerDay);
        Assert.Equal(20, store.GetNeed(mind, Need.Hunger, TicksPerDay));
        Assert.Equal(50, store.GetNeed(mind, Need.Hunger, TicksPerDay + (TicksPerDay / 2)));

        store.SatisfyNeed(mind, Need.Hunger, 500, 2UL * TicksPerDay);
        Assert.Equal(0, store.GetNeed(mind, Need.Hunger, 2UL * TicksPerDay));
    }

    [Fact]
    public void Many_small_reads_do_not_lose_time_to_rounding()
    {
        var (store, mind) = StoreWithMind(Calm);
        for (ulong tick = 0; tick <= TicksPerDay; tick += 7)
        {
            store.GetNeed(mind, Need.Hunger, tick);
        }

        Assert.Equal(60, store.GetNeed(mind, Need.Hunger, TicksPerDay));
    }

    [Fact]
    public void Invalid_need_operations_fail_loudly()
    {
        var (store, mind) = StoreWithMind(Calm);
        store.SatisfyNeed(mind, Need.Rest, 1, 100);

        Assert.Throws<ArgumentOutOfRangeException>(() => store.SatisfyNeed(mind, Need.Rest, -1, 200));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.GetNeed(mind, Need.Rest, 50));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.GetNeed(mind, (Need)9, 200));
    }
}
