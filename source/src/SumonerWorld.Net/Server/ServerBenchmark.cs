using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Net.Transport;

namespace SumonerWorld.Net.Server;

/// <summary>Settings of a <see cref="ServerBenchmark"/> run (M0-07 B2).</summary>
public sealed class ServerBenchmarkSettings
{
    /// <summary>Players that must be connected before the warm-up starts (B2: 16 bots).</summary>
    public int Players { get; init; } = 16;

    /// <summary>Ticks after all players are connected that do not count (B2: 30 s).</summary>
    public int WarmupTicks { get; init; } = 900;

    /// <summary>Ticks measured (B2: 10 min).</summary>
    public int MeasureTicks { get; init; } = 18_000;

    /// <summary>The bandwidth budget checked.</summary>
    public BandwidthBudget Budget { get; init; } = new();
}

/// <summary>Where a <see cref="ServerBenchmark"/> run is.</summary>
public enum ServerBenchmarkPhase
{
    /// <summary>Not all players are connected yet.</summary>
    WaitingForPlayers,

    /// <summary>Running, not measured.</summary>
    WarmingUp,

    /// <summary>Measuring.</summary>
    Measuring,

    /// <summary>All ticks measured; the host writes the results and stops.</summary>
    Done,
}

/// <summary>
/// Measures the dedicated server for B2 (M0-07 A2/A3): whole-tick time (receive, simulate, send), the creature AI stages, bytes
/// allocated on the server thread, collections, memory and per-client bandwidth, once all players are in and the warm-up is over.
/// <see cref="WireCounters"/> may read the network interface (bytes really sent and received, ENet headers and resends included).
/// The host calls <see cref="BeginTick"/> / <see cref="EndTick"/> around each <see cref="GameServer.Tick"/> and passes this as the
/// server's <see cref="IMetrics"/>.
/// </summary>
public sealed class ServerBenchmark : IMetrics
{
    private const double TickBudgetMs = 1000.0 / SimulationSettings.DefaultTicksPerSecond;
    private const double MeanTickLimitMs = 14;
    private const double P99TickLimitMs = 19;
    private const double P99AiLimitMs = 4;
    private const long ServerMemoryLimitBytes = 4L << 30;
    private const double BytesPerKiB = 1024;

    private readonly ServerBenchmarkSettings _settings;
    private readonly BandwidthMeter _meter;
    private readonly double[] _tickMs;
    private readonly double[] _aiMs;
    private readonly long[] _allocated;
    private readonly int[] _connected;
    private readonly long[] _stageStart = new long[Enum.GetValues<SimulationStage>().Length];
    private long _tickStart;
    private long _allocatedStart;
    private double _ai;
    private int _phaseTicks;
    private int _measured;
    private int[] _collectionsAtStart = [0, 0, 0];
    private long _wallStart;
    private TimeSpan _cpuStart;
    private (long Received, long Sent)? _wireStart;
    private long _peakWorkingSet;

    /// <summary>Creates a run; <paramref name="transport"/> is the server's metered transport.</summary>
    public ServerBenchmark(ServerBenchmarkSettings settings, MeteredTransport transport)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(transport);
        _settings = settings;
        _meter = new BandwidthMeter(transport);
        _tickMs = new double[settings.MeasureTicks];
        _aiMs = new double[settings.MeasureTicks];
        _allocated = new long[settings.MeasureTicks];
        _connected = new int[settings.MeasureTicks];
    }

    /// <summary>Reads total bytes received and sent on the server's network interface, or null where not available.</summary>
    public Func<(long Received, long Sent)?> WireCounters { get; init; } = () => null;

    /// <summary>Where the run is.</summary>
    public ServerBenchmarkPhase Phase { get; private set; }

    /// <summary>Call right before <see cref="GameServer.Tick"/>.</summary>
    public void BeginTick()
    {
        _ai = 0;
        _allocatedStart = GC.GetAllocatedBytesForCurrentThread();
        _tickStart = Stopwatch.GetTimestamp();
    }

    /// <summary>Call right after <see cref="GameServer.Tick"/> with the players connected now.</summary>
    public void EndTick(int connectedPlayers)
    {
        long end = Stopwatch.GetTimestamp();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - _allocatedStart;
        switch (Phase)
        {
            case ServerBenchmarkPhase.WaitingForPlayers when connectedPlayers >= _settings.Players:
                Phase = ServerBenchmarkPhase.WarmingUp;
                _phaseTicks = 0;
                break;
            case ServerBenchmarkPhase.WarmingUp when ++_phaseTicks >= _settings.WarmupTicks:
                StartMeasuring();
                break;
            case ServerBenchmarkPhase.Measuring:
                _tickMs[_measured] = Stopwatch.GetElapsedTime(_tickStart, end).TotalMilliseconds;
                _aiMs[_measured] = _ai;
                _allocated[_measured] = allocated;
                _connected[_measured] = connectedPlayers;
                _measured++;
                if (_measured % SimulationSettings.DefaultTicksPerSecond == 0)
                {
                    _meter.Sample();
                    _peakWorkingSet = Math.Max(_peakWorkingSet, Environment.WorkingSet);
                }

                if (_measured == _tickMs.Length)
                {
                    Phase = ServerBenchmarkPhase.Done;
                }

                break;
            default:
                break;
        }

        if (Phase is ServerBenchmarkPhase.WaitingForPlayers or ServerBenchmarkPhase.WarmingUp)
        {
            // Keep the meter's baselines current so the first measured second starts from zero.
            _meter.Sample();
            _meter.Restart();
        }
    }

    /// <inheritdoc />
    public void TickStarted(in SimulationTick tick)
    {
    }

    /// <inheritdoc />
    public void StageStarted(SimulationStage stage) => _stageStart[(int)stage] = Stopwatch.GetTimestamp();

    /// <inheritdoc />
    public void StageCompleted(SimulationStage stage)
    {
        if (stage is SimulationStage.Perception or SimulationStage.AiDecision or SimulationStage.Behaviour)
        {
            _ai += Stopwatch.GetElapsedTime(_stageStart[(int)stage]).TotalMilliseconds;
        }
    }

    /// <inheritdoc />
    public void TickCompleted(in SimulationTick tick)
    {
    }

    /// <summary>The results so far (complete once <see cref="Phase"/> is <see cref="ServerBenchmarkPhase.Done"/>).</summary>
    public ServerBenchmarkResult Result(TransactionCounts transactions)
    {
        int n = _measured;
        double[] ticks = _tickMs[..n];
        double[] ai = _aiMs[..n];
        Array.Sort(ticks);
        Array.Sort(ai);
        double wallSeconds = n == 0 ? 0 : Stopwatch.GetElapsedTime(_wallStart).TotalSeconds;
        double cpuPercent = wallSeconds == 0 ? 0 : (Process.GetCurrentProcess().TotalProcessorTime - _cpuStart).TotalSeconds / wallSeconds * 100;
        (long Received, long Sent)? wireNow = WireCounters();
        double? wireSent = null;
        double? wireReceived = null;
        if (_wireStart is { } start && wireNow is { } now && wallSeconds > 0)
        {
            wireSent = (now.Sent - start.Sent) / BytesPerKiB / wallSeconds;
            wireReceived = (now.Received - start.Received) / BytesPerKiB / wallSeconds;
        }

        BandwidthSummary bandwidth = _meter.Summarize();
        var result = new ServerBenchmarkResult
        {
            Phase = Phase,
            Players = _settings.Players,
            MeasuredTicks = n,
            WallSeconds = wallSeconds,
            MinConnected = n == 0 ? 0 : _connected[..n].Min(),
            TickMeanMs = n == 0 ? 0 : ticks.Average(),
            TickP50Ms = Percentile(ticks, 0.50),
            TickP99Ms = Percentile(ticks, 0.99),
            TickMaxMs = n == 0 ? 0 : ticks[^1],
            TickLoadP99Percent = Percentile(ticks, 0.99) / TickBudgetMs * 100,
            AiP99Ms = Percentile(ai, 0.99),
            AllocatedBytesPerTick = n == 0 ? 0 : _allocated[..n].Average(),
            Gen0Collections = GC.CollectionCount(0) - _collectionsAtStart[0],
            Gen1Collections = GC.CollectionCount(1) - _collectionsAtStart[1],
            Gen2Collections = GC.CollectionCount(2) - _collectionsAtStart[2],
            ProcessCpuPercent = cpuPercent,
            PeakWorkingSetMiB = Math.Max(_peakWorkingSet, Environment.WorkingSet) / BytesPerKiB / BytesPerKiB,
            Bandwidth = bandwidth,
            WireSentKiBps = wireSent,
            WireReceivedKiBps = wireReceived,
            Transactions = transactions,
        };

        // Network and completeness checks hold on any machine; timing checks only mean something on the benchmark machine (A4).
        var network = new List<string>(bandwidth.Exceeded(_settings.Budget));
        Check(network, Phase == ServerBenchmarkPhase.Done, $"run incomplete ({Phase}, {n} of {_tickMs.Length} ticks)");
        Check(network, result.MinConnected >= _settings.Players, $"only {result.MinConnected} of {_settings.Players} players connected at some point");
        if (wireSent is double sent)
        {
            Check(network, sent <= _settings.Budget.ServerTotal, $"wire sent {sent:F1} KiB/s > {_settings.Budget.ServerTotal} KiB/s");
            Check(network, sent / _settings.Players <= _settings.Budget.DownAverage, $"wire sent per client {sent / _settings.Players:F1} KiB/s > {_settings.Budget.DownAverage} KiB/s");
        }

        var timing = new List<string>();
        Check(timing, result.TickP99Ms <= P99TickLimitMs, $"tick p99 {result.TickP99Ms:F2} ms > {P99TickLimitMs} ms");
        Check(timing, result.TickMeanMs <= MeanTickLimitMs, $"tick mean {result.TickMeanMs:F2} ms > {MeanTickLimitMs} ms");
        Check(timing, result.AiP99Ms <= P99AiLimitMs, $"AI p99 {result.AiP99Ms:F2} ms > {P99AiLimitMs} ms");
        Check(timing, result.PeakWorkingSetMiB * BytesPerKiB * BytesPerKiB <= ServerMemoryLimitBytes, $"memory {result.PeakWorkingSetMiB:F0} MiB > 4 GiB");
        return result with { NetworkFailures = network, TimingFailures = timing };
    }

    /// <summary>Writes <c>summary.json</c> and the per-tick <c>ticks.csv</c> into <paramref name="directory"/>.</summary>
    public void Write(string directory, ServerBenchmarkResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "summary.json"), JsonSerializer.Serialize(result, JsonOptions));
        var csv = new StringBuilder("tick,tick_ms,ai_ms,allocated_bytes,connected\n");
        for (int i = 0; i < _measured; i++)
        {
            csv.Append(CultureInfo.InvariantCulture, $"{i},{_tickMs[i]:F4},{_aiMs[i]:F4},{_allocated[i]},{_connected[i]}\n");
        }

        File.WriteAllText(Path.Combine(directory, "ticks.csv"), csv.ToString());
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private static double Percentile(double[] sorted, double p) =>
        sorted.Length == 0 ? 0 : sorted[Math.Clamp((int)Math.Ceiling(sorted.Length * p) - 1, 0, sorted.Length - 1)];

    private static void Check(List<string> failures, bool passed, FormattableString failure)
    {
        if (!passed)
        {
            failures.Add(failure.ToString(CultureInfo.InvariantCulture));
        }
    }

    private void StartMeasuring()
    {
        Phase = ServerBenchmarkPhase.Measuring;
        _meter.Sample();
        _meter.Restart();
        _collectionsAtStart = [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];
        _wallStart = Stopwatch.GetTimestamp();
        _cpuStart = Process.GetCurrentProcess().TotalProcessorTime;
        _wireStart = WireCounters();
        _peakWorkingSet = Environment.WorkingSet;
    }
}

/// <summary>Intent counters of the server's transaction processor.</summary>
/// <param name="Applied">Applied intents.</param>
/// <param name="Rejected">Rejected intents.</param>
/// <param name="Duplicates">Repeats answered from the dedupe window.</param>
public readonly record struct TransactionCounts(int Applied, int Rejected, int Duplicates);

/// <summary>What a B2 run measured (written as <c>summary.json</c>).</summary>
public sealed record ServerBenchmarkResult
{
    /// <summary>Where the run ended.</summary>
    public ServerBenchmarkPhase Phase { get; init; }

    /// <summary>Players the run waited for.</summary>
    public int Players { get; init; }

    /// <summary>Ticks measured.</summary>
    public int MeasuredTicks { get; init; }

    /// <summary>Wall-clock length of the measured part.</summary>
    public double WallSeconds { get; init; }

    /// <summary>Fewest players connected in a measured tick.</summary>
    public int MinConnected { get; init; }

    /// <summary>Whole server tick (receive, simulate, send), mean.</summary>
    public double TickMeanMs { get; init; }

    /// <summary>Whole server tick, median.</summary>
    public double TickP50Ms { get; init; }

    /// <summary>Whole server tick, 99th percentile.</summary>
    public double TickP99Ms { get; init; }

    /// <summary>Whole server tick, slowest.</summary>
    public double TickMaxMs { get; init; }

    /// <summary>p99 tick as a share of the 33.3 ms tick.</summary>
    public double TickLoadP99Percent { get; init; }

    /// <summary>Perception + decision + behaviour stages, 99th percentile.</summary>
    public double AiP99Ms { get; init; }

    /// <summary>Bytes allocated on the server thread per tick, mean (includes the ENet adapter's packet copies).</summary>
    public double AllocatedBytesPerTick { get; init; }

    /// <summary>Gen 0 collections while measuring.</summary>
    public int Gen0Collections { get; init; }

    /// <summary>Gen 1 collections while measuring.</summary>
    public int Gen1Collections { get; init; }

    /// <summary>Gen 2 collections while measuring.</summary>
    public int Gen2Collections { get; init; }

    /// <summary>Process CPU time over wall time (100 = one core busy).</summary>
    public double ProcessCpuPercent { get; init; }

    /// <summary>Largest working set seen.</summary>
    public double PeakWorkingSetMiB { get; init; }

    /// <summary>Per-client bandwidth from the transport counters.</summary>
    public BandwidthSummary? Bandwidth { get; init; }

    /// <summary>Bytes really sent on the network interface, KiB/s (null where not readable).</summary>
    public double? WireSentKiBps { get; init; }

    /// <summary>Bytes really received on the network interface, KiB/s.</summary>
    public double? WireReceivedKiBps { get; init; }

    /// <summary>Intent counters at the end.</summary>
    public TransactionCounts Transactions { get; init; }

    /// <summary>Failed bandwidth and completeness thresholds (M0-07 A3); these hold on any machine.</summary>
    public IReadOnlyList<string> NetworkFailures { get; init; } = [];

    /// <summary>Failed tick, AI and memory thresholds; they count only on the benchmark machine (M0-07 A4, A5).</summary>
    public IReadOnlyList<string> TimingFailures { get; init; } = [];

    /// <summary>Every threshold met.</summary>
    public bool Passed => NetworkFailures.Count == 0 && TimingFailures.Count == 0;
}
