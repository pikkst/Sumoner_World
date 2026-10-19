namespace SumonerWorld.Core.Events;

/// <summary>
/// Per-tick typed event buffers between simulation systems (R10, TDD §2.2).
/// Events published during a tick are readable by later systems in the same tick and are cleared when the tick ends.
/// </summary>
public interface IEventBus
{
    /// <summary>Appends an event of a registered type to this tick's buffer.</summary>
    void Publish<TEvent>(in TEvent gameEvent)
        where TEvent : struct;

    /// <summary>Returns this tick's events of a registered type, in publish order.</summary>
    /// <remarks>Do not publish events of the same type while holding the returned span.</remarks>
    ReadOnlySpan<TEvent> Read<TEvent>()
        where TEvent : struct;
}
