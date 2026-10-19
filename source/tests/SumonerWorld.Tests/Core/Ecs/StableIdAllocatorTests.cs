using SumonerWorld.Core.Ecs;

namespace SumonerWorld.Tests.Core.Ecs;

public class StableIdAllocatorTests
{
    [Fact]
    public void A_new_world_issues_ids_from_one_upwards()
    {
        var allocator = new StableIdAllocator();

        Assert.Equal(new StableId(1), allocator.Allocate());
        Assert.Equal(new StableId(2), allocator.Allocate());
        Assert.Equal(2UL, allocator.LastIssued);
    }

    [Fact]
    public void A_loaded_world_continues_after_the_saved_high_water_mark()
    {
        var allocator = new StableIdAllocator(lastIssued: 41);

        Assert.Equal(new StableId(42), allocator.Allocate());
    }

    [Fact]
    public void The_empty_id_is_never_issued()
    {
        var allocator = new StableIdAllocator();

        Assert.False(StableId.None.IsAssigned);
        Assert.True(allocator.Allocate().IsAssigned);
    }

    [Fact]
    public void Exhausting_the_id_space_fails_instead_of_wrapping()
    {
        var allocator = new StableIdAllocator(ulong.MaxValue);

        Assert.Throws<InvalidOperationException>(() => allocator.Allocate());
    }
}
