using System.Globalization;
using SumonerWorld.Core.Diagnostics;

namespace SumonerWorld.Net.Hosting;

/// <summary>What the process should run.</summary>
public enum HostMode
{
    /// <summary>Normal game start (no networking yet).</summary>
    Game,

    /// <summary>Headless dedicated server (TDD §2.4, §3.1a).</summary>
    Server,

    /// <summary>Client that connects to a server.</summary>
    Client,

    /// <summary>A fleet of bot clients (B2, spike 2).</summary>
    Bots,

    /// <summary>The M1 test region with terrain and streaming, free-fly camera (M1-19).</summary>
    Region,

    /// <summary>The "Memory Grove" playtest build (M1-23): server and client in one process, a fresh grove every session.</summary>
    Playtest,
}

/// <summary>
/// Start-up options from command-line user arguments (after <c>--</c>) and environment variables (Docker, TDD §3.1a). Arguments win
/// over environment variables. Unknown arguments are an error, so typos do not silently change behaviour (R36).
/// </summary>
/// <remarks>
/// Arguments: <c>--server</c>, <c>--client</c>, <c>--connect=host:port</c>, <c>--port=7777</c>, <c>--account=1001</c>,
/// <c>--max-players=16</c>, <c>--exit-on-welcome</c>, <c>--quit-after-ticks=N</c>, <c>--bots=16</c> (with <c>--connect</c>),
/// <c>--benchmark-out=dir</c>, <c>--benchmark-players=16</c>, <c>--warmup-ticks=900</c>, <c>--measure-ticks=18000</c> (server, B2),
/// <c>--region</c>, <c>--region-chunks=8</c>, <c>--screenshot=file.png</c> (region: save a frame after streaming settled, then quit),
/// <c>--benchmark=B3</c> (region: fly the B3 route, write results to <c>--benchmark-out</c>, then quit),
/// <c>--showcase=grazer|predator|player|stages</c>, <c>--showcase-distance=30</c> (region: the six emotes, the player animations or the life stages, in rows seen from that distance),
/// <c>--dev-tools</c> (server: answer the dev overlay and AI Inspector; client: show them), <c>--inspect=nearest|click|tab|id</c>
/// (client: open the AI Inspector on start; <c>click</c> and <c>tab</c> go through the real controls as input events; with
/// <c>--screenshot</c> for a picture of it).
/// <c>--self-test</c> (client: walk to the nearest creature and use the interaction keys as input events),
/// <c>--playtest</c> (the Memory Grove playtest: a local server on <see cref="PlaytestPort"/> plus the client, fresh world).
/// Environment: <c>SW_SERVER_MODE</c> (any value starts the server), <c>SW_PORT</c>, <c>SW_DEV_SECRET</c>, <c>SW_MAX_PLAYERS</c>,
/// <c>SW_LOG_LEVEL</c>, <c>SW_DEV_TOOLS</c> (any value: as <c>--dev-tools</c>), <c>SW_HARDCORE</c> (any value: as <c>--hardcore</c>).
/// </remarks>
public sealed record HostOptions
{
    /// <summary>Default UDP game port (TDD §3.1a).</summary>
    public const int DefaultPort = 7777;

    /// <summary>Default player limit (GDD §8: 16 at EA).</summary>
    public const int DefaultMaxPlayers = 16;

    /// <summary>Port of the playtest's in-process server (localhost only).</summary>
    public const int PlaytestPort = 7790;

    /// <summary>Secret used by the dev server when none is configured. Dev and tests only.</summary>
    public const string DefaultDevSecret = "local-dev-secret";

    /// <summary>What to run.</summary>
    public HostMode Mode { get; init; } = HostMode.Game;

    /// <summary>Server: port to listen on. Client: port to connect to.</summary>
    public int Port { get; init; } = DefaultPort;

    /// <summary>Client: server address.</summary>
    public string Address { get; init; } = "127.0.0.1";

    /// <summary>Client: account id for the dev ticket.</summary>
    public ulong Account { get; init; } = 1001;

    /// <summary>Dev authentication secret (server and client must match).</summary>
    public string DevSecret { get; init; } = DefaultDevSecret;

    /// <summary>Server: player limit.</summary>
    public int MaxPlayers { get; init; } = DefaultMaxPlayers;

    /// <summary>Client: quit with code 0 once the server has welcomed us and the first snapshot arrived (smoke tests).</summary>
    public bool ExitOnWelcome { get; init; }

    /// <summary>Server: path of the world database (empty = the user data folder).</summary>
    public string WorldPath { get; init; } = string.Empty;

    /// <summary>Server: quit after this many ticks (0 = run forever).</summary>
    public ulong QuitAfterTicks { get; init; }

    /// <summary>Bots: how many.</summary>
    public int Bots { get; init; }

    /// <summary>Server: write B2 results into this folder and stop when measured (empty = no benchmark).</summary>
    public string BenchmarkOut { get; init; } = string.Empty;

    /// <summary>Server benchmark: players to wait for.</summary>
    public int BenchmarkPlayers { get; init; } = DefaultMaxPlayers;

    /// <summary>Server benchmark: ticks after all players joined that are not measured.</summary>
    public int WarmupTicks { get; init; } = 900;

    /// <summary>Server benchmark: ticks measured.</summary>
    public int MeasureTicks { get; init; } = 18_000;

    /// <summary>Region: chunks per side (8 = the 512 m test region, 64 = 4 km).</summary>
    public int RegionChunks { get; init; } = 8;

    /// <summary>Region: save one frame here once streaming has settled, then quit (empty = keep running).</summary>
    public string Screenshot { get; init; } = string.Empty;

    /// <summary>Region: showcase model (<c>grazer</c> or <c>predator</c> emotes, <c>player</c> animations, <c>stages</c>), empty for none.</summary>
    public string Showcase { get; init; } = string.Empty;

    /// <summary>Region: showcase camera distance in metres (style guide §4: emotes must read at 30 m).</summary>
    public int ShowcaseDistance { get; init; } = 30;

    /// <summary>Region: client benchmark to run (<c>B3</c>), empty for none.</summary>
    public string Benchmark { get; init; } = string.Empty;

    /// <summary>Dev tools (M1-22): the server answers dev overlay and AI Inspector requests; the client shows them.</summary>
    public bool DevTools { get; init; }

    /// <summary>Run the server with the Hardcore preset (<c>--hardcore</c> or <c>SW_HARDCORE</c>): no lock-on.</summary>
    public bool Hardcore { get; init; }

    /// <summary>
    /// Client: open the AI Inspector at start: <c>nearest</c>, a stable id, or through the controls as input events (<c>click</c>:
    /// F3 hides the overlay, then a left click on the nearest creature; <c>tab</c>: Tab twice). Empty for none.
    /// </summary>
    public string Inspect { get; init; } = string.Empty;

    /// <summary>Client: play the start of a Memory Grove session through the real controls, then take the screenshot (M1-23).</summary>
    public bool SelfTest { get; init; }

    /// <summary>Lowest log level written.</summary>
    public LogLevel LogLevel { get; init; } = LogLevel.Info;

    /// <summary>Parses arguments and environment.</summary>
    public static HostOptions Parse(IReadOnlyList<string> arguments, Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(environment);

        var options = new HostOptions
        {
            Mode = string.IsNullOrEmpty(environment("SW_SERVER_MODE")) ? HostMode.Game : HostMode.Server,
            Port = environment("SW_PORT") is string port ? ParseInt(port, "SW_PORT") : DefaultPort,
            DevSecret = environment("SW_DEV_SECRET") is { Length: > 0 } secret ? secret : DefaultDevSecret,
            MaxPlayers = environment("SW_MAX_PLAYERS") is string max ? ParseInt(max, "SW_MAX_PLAYERS") : DefaultMaxPlayers,
            LogLevel = environment("SW_LOG_LEVEL") is string level ? Enum.Parse<LogLevel>(level, ignoreCase: true) : LogLevel.Info,
            WorldPath = environment("SW_WORLD") ?? string.Empty,
            DevTools = !string.IsNullOrEmpty(environment("SW_DEV_TOOLS")),
            Hardcore = !string.IsNullOrEmpty(environment("SW_HARDCORE")),
        };

        foreach (string argument in arguments)
        {
            options = Apply(options, argument);
        }

        return options;
    }

    private static HostOptions Apply(HostOptions options, string argument)
    {
        string[] parts = argument.Split('=', 2);
        string value = parts.Length == 2 ? parts[1] : string.Empty;
        return parts[0] switch
        {
            "--server" => options with { Mode = HostMode.Server },
            "--client" => options with { Mode = HostMode.Client },
            "--connect" => ParseEndpoint(options with { Mode = options.Mode == HostMode.Bots ? HostMode.Bots : HostMode.Client }, value),
            "--port" => options with { Port = ParseInt(value, argument) },
            "--account" => options with { Account = ulong.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture) },
            "--max-players" => options with { MaxPlayers = ParseInt(value, argument) },
            "--exit-on-welcome" => options with { ExitOnWelcome = true },
            "--dev-tools" => options with { DevTools = true },
            "--hardcore" => options with { Hardcore = true },
            "--self-test" => options with { SelfTest = true },
            "--inspect" when value is "nearest" or "click" or "tab" || ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _) => options with { DevTools = true, Inspect = value },
            "--world" when value.Length > 0 => options with { WorldPath = value },
            "--region" => options with { Mode = HostMode.Region },
            "--playtest" => options with { Mode = HostMode.Playtest, Port = PlaytestPort, Address = "127.0.0.1" },
            "--benchmark" when value == "B3" => options with { Mode = HostMode.Region, Benchmark = value },
            "--showcase" when value is "grazer" or "predator" or "player" or "stages" => options with { Mode = HostMode.Region, Showcase = value },
            "--showcase-distance" => options with { ShowcaseDistance = ParseInt(value, argument) },
            "--region-chunks" => options with { RegionChunks = ParseInt(value, argument) },
            "--screenshot" when value.Length > 0 => options with { Screenshot = value },
            "--bots" => options with { Mode = HostMode.Bots, Bots = ParseInt(value, argument) },
            "--benchmark-out" when value.Length > 0 => options with { BenchmarkOut = value },
            "--benchmark-players" => options with { BenchmarkPlayers = ParseInt(value, argument) },
            "--warmup-ticks" => options with { WarmupTicks = ParseInt(value, argument) },
            "--measure-ticks" => options with { MeasureTicks = ParseInt(value, argument) },
            "--quit-after-ticks" => options with { QuitAfterTicks = ulong.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture) },
            _ => throw new ArgumentException($"Unknown argument '{argument}'.", nameof(argument)),
        };
    }

    private static HostOptions ParseEndpoint(HostOptions options, string value)
    {
        int colon = value.LastIndexOf(':');
        return colon > 0
            ? options with { Address = value[..colon], Port = ParseInt(value[(colon + 1)..], "--connect") }
            : throw new ArgumentException($"--connect needs host:port, got '{value}'.", nameof(value));
    }

    private static int ParseInt(string value, string name) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) && parsed > 0
            ? parsed
            : throw new ArgumentException($"{name} must be a positive integer, got '{value}'.", nameof(value));
}
