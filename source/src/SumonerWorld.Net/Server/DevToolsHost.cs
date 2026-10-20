using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.World;
using SumonerWorld.Net.Messages;
using SumonerWorld.Net.Transport;

namespace SumonerWorld.Net.Server;

/// <summary>
/// The server side of the dev tools (M1-22), used by <see cref="GameServer"/> when <see cref="GameServerSettings.DevTools"/> is on:
/// keeps each connection's <see cref="DevRequest"/>, times every tick (<see cref="ServerTickMetrics"/>) and, every
/// <see cref="GameServer.DevReportEveryTicks"/>, sends the <see cref="ServerMetrics"/> and the requested <see cref="DevInspection"/>.
/// Inspections are built between ticks from read-only queries (<see cref="CreatureInspector"/>), so they never change the simulation.
/// </summary>
public sealed class DevToolsHost
{
    private readonly CreatureWorld _world;
    private readonly ITransport _transport;
    private readonly MeteredTransport? _metered;
    private readonly CreatureInspector _inspector;
    private readonly Dictionary<ConnectionId, Subscriber> _subscribers = [];
    private readonly byte[] _buffer = new byte[Protocol.MaxMessageSize];
    private readonly DevRequestCodec _requestCodec = new();
    private readonly ServerMetricsCodec _metricsCodec = new();
    private readonly DevInspectionCodec _inspectionCodec = new();
    private long _sentBytes;

    /// <summary>Creates the dev tools; <paramref name="next"/> also receives the stage calls (the B2 benchmark, if any).</summary>
    public DevToolsHost(CreatureWorld world, ITransport transport, IMetrics? next)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(transport);
        _world = world;
        _transport = transport;
        _metered = transport as MeteredTransport;
        _inspector = new CreatureInspector(world);
        Metrics = new ServerTickMetrics(next);
    }

    /// <summary>The tick timer (the scheduler's <see cref="IMetrics"/>).</summary>
    public ServerTickMetrics Metrics { get; }

    /// <summary>Connections that asked for something.</summary>
    public int Subscribers => _subscribers.Count;

    /// <summary>Reads a request from a client in a session; a request for nothing unsubscribes.</summary>
    public void OnRequest(ConnectionId connection, ref MessageReader reader)
    {
        DevRequest request = _requestCodec.Read(ref reader);
        if (!request.Metrics && request.Inspect == 0)
        {
            _subscribers.Remove(connection);
            return;
        }

        _subscribers[connection] = new Subscriber(request, _metered?.For(connection).WireBytesSent ?? 0);
    }

    /// <summary>The connection is gone (suspended or closed); a resumed client asks again.</summary>
    public void Forget(ConnectionId connection) => _subscribers.Remove(connection);

    /// <summary>Sends the reports for the window that ends at <paramref name="tick"/>.</summary>
    public void Report(ulong tick, int connectedPlayers)
    {
        (int ticks, StageTiming whole, StageTiming[] stages, long allocated) = Metrics.TakeWindow();
        long sent = _metered?.Total.WireBytesSent ?? 0;
        long perSecond = SimulationSettings.DefaultTicksPerSecond / Math.Max(1, ticks);
        var report = new ServerMetrics(
            tick, ticks, whole, stages, allocated, [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)], CountTiers(),
            connectedPlayers, (sent - _sentBytes) * perSecond, 0, (int)(Environment.WorkingSet >> 20));
        _sentBytes = sent;
        foreach ((ConnectionId connection, Subscriber subscriber) in _subscribers)
        {
            if (subscriber.Request.Metrics)
            {
                long client = _metered?.For(connection).WireBytesSent ?? 0;
                var writer = new MessageWriter(_buffer);
                _metricsCodec.Write(ref writer, report with { ClientBytesPerSecond = (client - subscriber.SentBytes) * perSecond });
                _transport.Send(connection, Channel.Unreliable, writer.WrittenSpan);
                subscriber.SentBytes = client;
            }

            if (subscriber.Request.Inspect != 0)
            {
                var writer = new MessageWriter(_buffer);
                _inspectionCodec.Write(ref writer, new DevInspection(subscriber.Request.Inspect, _inspector.Inspect(new StableId(subscriber.Request.Inspect))));
                _transport.Send(connection, Channel.Unreliable, writer.WrittenSpan);
            }
        }
    }

    private int[] CountTiers()
    {
        int[] tiers = new int[ServerMetrics.TierCount];
        foreach (AiTier tier in _world.World.Store<AiTier>().Components)
        {
            tiers[(int)tier.Tier]++;
        }

        return tiers;
    }

    private sealed class Subscriber(DevRequest request, long sentBytes)
    {
        public DevRequest Request { get; } = request;

        public long SentBytes { get; set; } = sentBytes;
    }
}
