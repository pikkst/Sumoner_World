using Godot;
using SumonerWorld.Game.Diagnostics;

using SumonerWorld.Net.Hosting;

namespace SumonerWorld.Game.Hosting;

/// <summary>
/// Entry point of <c>Main.tscn</c>: reads the start-up options and starts the dedicated server, a client, or the normal game
/// (currently the lighting test scene). Bad options stop the process with exit code 64 and a clear message (R36).
/// </summary>
public sealed partial class Bootstrap : Node
{
    private const int ExitBadOptions = 64;
    private const string GameScene = "res://scenes/dev/LightingTest.tscn";

    /// <inheritdoc />
    public override void _Ready()
    {
        HostOptions options;
        try
        {
            options = HostOptions.Parse(OS.GetCmdlineUserArgs(), name => System.Environment.GetEnvironmentVariable(name));
        }
        catch (ArgumentException exception)
        {
            GD.PrintErr($"Invalid start-up options: {exception.Message}");
            GetTree().Quit(ExitBadOptions);
            return;
        }

        var log = new GodotLog(options.LogLevel);
        switch (options.Mode)
        {
            case HostMode.Server:
                AddChild(new ServerHost(options, log));
                break;
            case HostMode.Client:
                AddChild(new ClientHost(options, log));
                break;
            case HostMode.Bots:
                AddChild(new BotHost(options, log));
                break;
            case HostMode.Region:
                AddChild(new RegionHost(options, log));
                break;
            case HostMode.Playtest:
                // Memory Grove (M1-23): the server first, so it listens before the client connects to it over localhost.
                AddChild(new ServerHost(options, log));
                AddChild(new ClientHost(options, log));
                break;
            default:
                GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, GameScene);
                break;
        }
    }
}
