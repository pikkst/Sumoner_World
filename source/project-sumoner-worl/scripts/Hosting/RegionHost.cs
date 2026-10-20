using Godot;
using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Game.World;
using SumonerWorld.Net.Hosting;

namespace SumonerWorld.Game.Hosting;

/// <summary>
/// Opens the M1 test region (<c>--region</c>, M1-19). With <see cref="HostOptions.Screenshot"/> it waits until streaming has caught up
/// (nothing pending or in flight, at least <see cref="MinFrames"/> frames), saves the frame as PNG and quits; used to check the
/// terrain and props without the editor. With <c>--benchmark=B3</c> it runs the <see cref="FlyRouteBenchmark"/>.
/// </summary>
public sealed partial class RegionHost : Node
{
    /// <summary>Frames rendered before a screenshot, so shadows and the terrain clipmap settle.</summary>
    public const int MinFrames = 120;

    private readonly HostOptions _options;
    private readonly ILog _log;
    private TestRegion? _region;
    private int _frames;

    /// <summary>Creates the host.</summary>
    public RegionHost(HostOptions options, ILog log)
    {
        _options = options;
        _log = log;
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        _region = new TestRegion(_options.RegionChunks, _log);
        AddChild(_region);
        if (_options.Benchmark == "B3")
        {
            string output = _options.BenchmarkOut.Length > 0 ? _options.BenchmarkOut : ProjectSettings.GlobalizePath("user://b3");
            AddChild(new FlyRouteBenchmark(_region, output, _log));
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_options.Screenshot.Length == 0 || _region?.Streamer is not { } streamer || _region.Camera is null)
        {
            return;
        }

        bool settled = streamer.Backlog == 0;
        if (++_frames >= MinFrames && settled)
        {
            Image image = GetViewport().GetTexture().GetImage();
            Error error = image.SavePng(_options.Screenshot);
            _log.Write(error == Error.Ok ? LogLevel.Info : LogLevel.Error, "region.screenshot", $"{_options.Screenshot}: {error}, {streamer.Applied} chunks streamed");
            GetTree().Quit(error == Error.Ok ? 0 : 1);
        }
    }
}
