using System.Globalization;

namespace SumonerWorld.Net.Transport;

/// <summary>The TDD §3.2 bandwidth budget, in KiB/s of estimated wire traffic.</summary>
public sealed record BandwidthBudget
{
    /// <summary>Per client, server → client, averaged over the run.</summary>
    public double DownAverage { get; init; } = 64;

    /// <summary>Per client, server → client, in any one second.</summary>
    public double DownBurst { get; init; } = 128;

    /// <summary>Per client, client → server, averaged over the run.</summary>
    public double UpAverage { get; init; } = 8;

    /// <summary>Everything the server sends, averaged over the run (1.5 MB/s for 16 players).</summary>
    public double ServerTotal { get; init; } = 1536;
}

/// <summary>Per-second bandwidth over a run, in KiB/s of estimated wire traffic (<see cref="TrafficCounters.WireBytesSent"/>).</summary>
/// <param name="Seconds">Seconds sampled.</param>
/// <param name="ClientSeconds">Client-seconds sampled (one per connection with traffic in a second).</param>
/// <param name="DownMean">Per client, server → client, mean.</param>
/// <param name="DownP99">Per client, server → client, 99th percentile second.</param>
/// <param name="DownMax">Per client, server → client, busiest second.</param>
/// <param name="UpMean">Per client, client → server, mean.</param>
/// <param name="UpMax">Per client, client → server, busiest second.</param>
/// <param name="ServerSentMean">Everything the server sent, mean per second.</param>
/// <param name="ServerSentMax">Everything the server sent, busiest second.</param>
/// <param name="PayloadDownMean">Per client, server → client payload only (no header estimate), mean.</param>
public sealed record BandwidthSummary(
    int Seconds,
    int ClientSeconds,
    double DownMean,
    double DownP99,
    double DownMax,
    double UpMean,
    double UpMax,
    double ServerSentMean,
    double ServerSentMax,
    double PayloadDownMean)
{
    /// <summary>Which budget lines are exceeded (empty when within budget).</summary>
    public IReadOnlyList<string> Exceeded(BandwidthBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        var exceeded = new List<string>();
        Check(exceeded, "down average", DownMean, budget.DownAverage);
        Check(exceeded, "down burst", DownMax, budget.DownBurst);
        Check(exceeded, "up average", UpMean, budget.UpAverage);
        Check(exceeded, "server total", ServerSentMean, budget.ServerTotal);
        return exceeded;
    }

    private static void Check(List<string> exceeded, string name, double value, double limit)
    {
        if (value > limit)
        {
            exceeded.Add(string.Create(CultureInfo.InvariantCulture, $"{name} {value:F1} KiB/s > {limit:F0} KiB/s"));
        }
    }
}

/// <summary>
/// Samples a server's <see cref="MeteredTransport"/> once per second (B2, TDD §3.2): how much each connection sent and received in
/// that second. <see cref="Restart"/> drops the samples so far (end of warm-up) without losing the per-connection baselines.
/// </summary>
public sealed class BandwidthMeter
{
    private const double BytesPerKiB = 1024;

    private readonly MeteredTransport _transport;
    private readonly List<long> _lastDown = [];
    private readonly List<long> _lastUp = [];
    private readonly List<long> _lastPayloadDown = [];
    private readonly List<double> _down = [];
    private readonly List<double> _up = [];
    private readonly List<double> _serverSent = [];
    private double _payloadDown;
    private long _lastServerSent;

    /// <summary>Meters <paramref name="transport"/> (the server's).</summary>
    public BandwidthMeter(MeteredTransport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        _transport = transport;
    }

    /// <summary>Seconds sampled since the start or the last <see cref="Restart"/>.</summary>
    public int Seconds => _serverSent.Count;

    /// <summary>Records one second (<paramref name="seconds"/> = how long it really was, for hosts whose ticks drift).</summary>
    public void Sample(double seconds = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seconds);
        for (int i = 0; i < _transport.ConnectionSlots; i++)
        {
            TrafficCounters counters = _transport.For(new ConnectionId(i));
            while (_lastDown.Count <= i)
            {
                _lastDown.Add(0);
                _lastUp.Add(0);
                _lastPayloadDown.Add(0);
            }

            long down = counters.WireBytesSent - _lastDown[i];
            long up = counters.WireBytesReceived - _lastUp[i];
            long payloadDown = counters.BytesSent - _lastPayloadDown[i];
            (_lastDown[i], _lastUp[i], _lastPayloadDown[i]) = (counters.WireBytesSent, counters.WireBytesReceived, counters.BytesSent);
            if (down > 0 || up > 0)
            {
                _down.Add(down / BytesPerKiB / seconds);
                _up.Add(up / BytesPerKiB / seconds);
                _payloadDown += payloadDown / BytesPerKiB / seconds;
            }
        }

        long sent = _transport.Total.WireBytesSent;
        _serverSent.Add((sent - _lastServerSent) / BytesPerKiB / seconds);
        _lastServerSent = sent;
    }

    /// <summary>Forgets the samples so far; the next sample counts from now.</summary>
    public void Restart()
    {
        _down.Clear();
        _up.Clear();
        _serverSent.Clear();
        _payloadDown = 0;
    }

    /// <summary>The samples so far.</summary>
    public BandwidthSummary Summarize()
    {
        double[] down = [.. _down];
        Array.Sort(down);
        return new BandwidthSummary(
            _serverSent.Count,
            down.Length,
            Mean(_down),
            down.Length == 0 ? 0 : down[Math.Min(down.Length - 1, (int)Math.Ceiling(down.Length * 0.99) - 1)],
            down.Length == 0 ? 0 : down[^1],
            Mean(_up),
            _up.Count == 0 ? 0 : _up.Max(),
            Mean(_serverSent),
            _serverSent.Count == 0 ? 0 : _serverSent.Max(),
            down.Length == 0 ? 0 : _payloadDown / down.Length);
    }

    private static double Mean(List<double> values) => values.Count == 0 ? 0 : values.Average();
}
