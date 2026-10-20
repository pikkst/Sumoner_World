using System.Globalization;
using System.Text;
using System.Text.Json;
using Godot;
using SumonerWorld.Core.Diagnostics;

namespace SumonerWorld.Game.World;

/// <summary>What a B3 run measured (written as <c>summary.json</c>).</summary>
public sealed record FlyRouteResult
{
    /// <summary>Chunks per side of the region flown.</summary>
    public int RegionChunks { get; init; }

    /// <summary>Frames measured.</summary>
    public int Frames { get; init; }

    /// <summary>Seconds measured.</summary>
    public double Seconds { get; init; }

    /// <summary>Metres flown.</summary>
    public double Metres { get; init; }

    /// <summary>Frame time, median.</summary>
    public double FrameP50Ms { get; init; }

    /// <summary>Frame time, 95th percentile.</summary>
    public double FrameP95Ms { get; init; }

    /// <summary>Frame time, 99th percentile.</summary>
    public double FrameP99Ms { get; init; }

    /// <summary>Slowest frame.</summary>
    public double FrameMaxMs { get; init; }

    /// <summary>Share of frames over 16.7 ms, in percent.</summary>
    public double Over16Percent { get; init; }

    /// <summary>Frames over 33.3 ms.</summary>
    public int Over33Frames { get; init; }

    /// <summary>Frames over 33.3 ms in which, or right after which, chunks were uploaded (B3: must be 0).</summary>
    public int StreamingHitches { get; init; }

    /// <summary>Chunks uploaded while measuring.</summary>
    public long ChunksStreamed { get; init; }

    /// <summary>Longest main-thread upload in one frame.</summary>
    public double MaxUploadMs { get; init; }

    /// <summary>Most chunks waiting at once (planned, building or waiting to upload).</summary>
    public int MaxBacklog { get; init; }

    /// <summary>Largest static memory use reported by Godot, MiB.</summary>
    public double PeakStaticMemoryMiB { get; init; }

    /// <summary>B3: no streaming hitch over 33.3 ms.</summary>
    public bool B3Passed => StreamingHitches == 0;

    /// <summary>Spike 4: no frame at all over 33.3 ms.</summary>
    public bool Spike4Passed => Over33Frames == 0;
}

/// <summary>
/// Benchmark B3 "Stream-Run" (M0-07 A1; TDD §8 spike 4): after a 5 s settle (already flying, not measured), the camera flies a fixed loop at eye height (2 m above
/// the ground) for 150 s at sprint speed (<see cref="SprintSpeed"/>) and 150 s at 3× sprint, looking where it goes. Every frame's time,
/// chunk uploads and upload time are recorded; a frame over 33.3 ms with an upload in it or the frame before counts as a streaming
/// hitch. Writes <c>frames.csv</c> and <c>summary.json</c>, then quits (exit 0 when B3 passed).
/// </summary>
public sealed partial class FlyRouteBenchmark : Node
{
    /// <summary>Assumed sprint: 1.5 × the 6 m/s walk (no sprint value is designed yet).</summary>
    public const float SprintSpeed = 9f;

    private const double SettleSeconds = 5;
    private const double LegSeconds = 150;
    private const float EyeHeight = 2f;
    private const double HitchMs = 1000.0 / 30;
    private const double SmoothMs = 1000.0 / 60;
    private const float Margin = 64f;

    private readonly TestRegion _region;
    private readonly string _outDirectory;
    private readonly ILog _log;
    private readonly List<Frame> _frames = new(40_000);
    private Vector3[] _route = [];
    private int _leg;
    private double _elapsed;
    private double _metres;
    private ulong _lastTicks;
    private int _lastApplied;
    private int _maxBacklog;
    private double _peakMemory;

    /// <summary>Creates the benchmark for a region; results go to <paramref name="outDirectory"/>.</summary>
    public FlyRouteBenchmark(TestRegion region, string outDirectory, ILog log)
    {
        _region = region;
        _outDirectory = outDirectory;
        _log = log;
        Name = "FlyRouteBenchmark";
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        _region.FreeFly = false;
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = 0;
        float size = _region.Field!.Size;
        float lo = Margin;
        float hi = size - Margin;
        float mid = size / 2;

        // A loop through the corners and across the middle, so the flight crosses every part of the region.
        _route = [new(lo, 0, lo), new(hi, 0, mid), new(hi, 0, hi), new(mid, 0, lo), new(lo, 0, hi), new(lo, 0, lo)];
        _region.Camera!.Position = Ground(_route[0]);
        _lastTicks = Time.GetTicksUsec();
        _log.Write(LogLevel.Info, "b3.started", $"{size} m region, route {RouteLength():F0} m per lap, {SprintSpeed} m/s then {SprintSpeed * 3} m/s");
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        ulong now = Time.GetTicksUsec();
        double frameMs = (now - _lastTicks) / 1000.0;
        _lastTicks = now;
        _elapsed += delta;
        if (_elapsed < SettleSeconds)
        {
            // Settle while already flying, so first-time shader and pipeline compiles fall into the excluded warm-up (M0-07 A4).
            Fly(SprintSpeed * (float)delta);
            _metres = 0;
            return;
        }

        double measured = _elapsed - SettleSeconds;
        if (measured >= 2 * LegSeconds)
        {
            Finish();
            return;
        }

        float speed = measured < LegSeconds ? SprintSpeed : SprintSpeed * 3;
        Fly(speed * (float)delta);
        ChunkStreamer streamer = _region.Streamer!;
        int backlog = streamer.Backlog;
        _maxBacklog = Math.Max(_maxBacklog, backlog);
        _peakMemory = Math.Max(_peakMemory, Performance.GetMonitor(Performance.Monitor.MemoryStatic) / 1048576.0);
        _frames.Add(new Frame(measured, frameMs, streamer.AppliedLastFrame, streamer.ApplyMsLastFrame, backlog, _lastApplied > 0));
        _lastApplied = streamer.AppliedLastFrame;
    }

    private void Fly(float distance)
    {
        Camera3D camera = _region.Camera!;
        _metres += distance;
        Vector3 position = camera.Position with { Y = 0 };
        while (distance > 0)
        {
            Vector3 target = _route[(_leg + 1) % _route.Length];
            Vector3 toTarget = target - position;
            float left = toTarget.Length();
            if (left <= distance)
            {
                position = target;
                distance -= left;
                _leg = (_leg + 1) % (_route.Length - 1);
            }
            else
            {
                position += toTarget / left * distance;
                distance = 0;
            }
        }

        Vector3 ahead = _route[(_leg + 1) % _route.Length] - position;
        camera.Position = Ground(position);
        if (ahead.LengthSquared() > 1)
        {
            camera.LookAt(camera.Position + (ahead.Normalized() * 10) + new Vector3(0, -1, 0), Vector3.Up);
        }
    }

    private Vector3 Ground(Vector3 position) => position with { Y = _region.Field!.HeightAt(position.X, position.Z) + EyeHeight };

    private double RouteLength()
    {
        double length = 0;
        for (int i = 0; i + 1 < _route.Length; i++)
        {
            length += _route[i].DistanceTo(_route[i + 1]);
        }

        return length;
    }

    private void Finish()
    {
        double[] times = [.. _frames.Select(frame => frame.Ms)];
        Array.Sort(times);
        var result = new FlyRouteResult
        {
            RegionChunks = (int)(_region.Field!.Size / Core.World.ChunkCoord.Size),
            Frames = times.Length,
            Seconds = 2 * LegSeconds,
            Metres = _metres,
            FrameP50Ms = Percentile(times, 0.50),
            FrameP95Ms = Percentile(times, 0.95),
            FrameP99Ms = Percentile(times, 0.99),
            FrameMaxMs = times.Length == 0 ? 0 : times[^1],
            Over16Percent = times.Length == 0 ? 0 : 100.0 * times.Count(ms => ms > SmoothMs) / times.Length,
            Over33Frames = times.Count(ms => ms > HitchMs),
            StreamingHitches = _frames.Count(frame => frame.Ms > HitchMs && (frame.Applied > 0 || frame.AppliedBefore)),
            ChunksStreamed = _frames.Sum(frame => (long)frame.Applied),
            MaxUploadMs = _frames.Count == 0 ? 0 : _frames.Max(frame => frame.UploadMs),
            MaxBacklog = _maxBacklog,
            PeakStaticMemoryMiB = _peakMemory,
        };

        Directory.CreateDirectory(_outDirectory);
        File.WriteAllText(Path.Combine(_outDirectory, "summary.json"), JsonSerializer.Serialize(result, JsonOptions));
        var csv = new StringBuilder("t_s,frame_ms,chunks_applied,upload_ms,backlog\n");
        foreach (Frame frame in _frames)
        {
            csv.Append(CultureInfo.InvariantCulture, $"{frame.Time:F4},{frame.Ms:F3},{frame.Applied},{frame.UploadMs:F3},{frame.Backlog}\n");
        }

        File.WriteAllText(Path.Combine(_outDirectory, "frames.csv"), csv.ToString());
        _log.Write(result.B3Passed ? LogLevel.Info : LogLevel.Warning, "b3.finished",
            string.Create(CultureInfo.InvariantCulture, $"{result.Frames} frames, p99 {result.FrameP99Ms:F2} ms, max {result.FrameMaxMs:F1} ms, {result.Over33Frames} over 33 ms, {result.StreamingHitches} streaming hitches; B3 {(result.B3Passed ? "passed" : "failed")}, spike 4 {(result.Spike4Passed ? "passed" : "failed")}"));
        SetProcess(false);
        GetTree().Quit(result.B3Passed ? 0 : 1);
    }

    private static double Percentile(double[] sorted, double p) =>
        sorted.Length == 0 ? 0 : sorted[Math.Clamp((int)Math.Ceiling(sorted.Length * p) - 1, 0, sorted.Length - 1)];

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private readonly record struct Frame(double Time, double Ms, int Applied, double UploadMs, int Backlog, bool AppliedBefore);
}
