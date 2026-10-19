using SumonerWorld.Core.Events;

namespace SumonerWorld.Tests.Core.Events;

public class EventBusTests
{
    private const int Capacity = 4;

    [Fact]
    public void Events_are_read_back_in_publish_order()
    {
        var bus = new EventBus();
        bus.Register<TestEvent>(Capacity);

        bus.Publish(new TestEvent(1));
        bus.Publish(new TestEvent(2));
        bus.Publish(new TestEvent(3));

        Assert.Equal([new TestEvent(1), new TestEvent(2), new TestEvent(3)], bus.Read<TestEvent>().ToArray());
    }

    [Fact]
    public void End_of_tick_clears_every_buffer()
    {
        var bus = new EventBus();
        bus.Register<TestEvent>(Capacity);
        bus.Register<OtherEvent>(Capacity);
        bus.Publish(new TestEvent(1));
        bus.Publish(new OtherEvent(true));

        bus.EndTick();

        Assert.True(bus.Read<TestEvent>().IsEmpty);
        Assert.True(bus.Read<OtherEvent>().IsEmpty);
    }

    [Fact]
    public void Overflowing_the_capacity_keeps_all_events_and_counts_the_growth()
    {
        var bus = new EventBus();
        bus.Register<TestEvent>(Capacity);

        for (int i = 0; i <= Capacity; i++)
        {
            bus.Publish(new TestEvent(i));
        }

        Assert.Equal(Capacity + 1, bus.Read<TestEvent>().Length);
        Assert.Equal(1, bus.CapacityGrowths);
    }

    [Fact]
    public void Unregistered_event_types_fail_loudly()
    {
        var bus = new EventBus();

        var error = Assert.Throws<InvalidOperationException>(() => bus.Publish(new TestEvent(1)));
        Assert.Contains(nameof(TestEvent), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Registering_a_type_twice_is_rejected()
    {
        var bus = new EventBus();
        bus.Register<TestEvent>(Capacity);

        Assert.Throws<InvalidOperationException>(() => bus.Register<TestEvent>(Capacity));
    }

    private readonly record struct TestEvent(int Value);

    private readonly record struct OtherEvent(bool Flag);
}
