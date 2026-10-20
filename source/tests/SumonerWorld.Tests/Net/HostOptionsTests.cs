using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Net.Hosting;

namespace SumonerWorld.Tests.Net;

public class HostOptionsTests
{
    private static readonly Func<string, string?> NoEnvironment = _ => null;

    [Fact]
    public void Defaults_start_the_normal_game_on_the_standard_port()
    {
        HostOptions options = HostOptions.Parse([], NoEnvironment);

        Assert.Equal(HostMode.Game, options.Mode);
        Assert.Equal(HostOptions.DefaultPort, options.Port);
        Assert.Equal(HostOptions.DefaultMaxPlayers, options.MaxPlayers);
    }

    [Fact]
    public void The_docker_environment_starts_a_configured_server()
    {
        var environment = new Dictionary<string, string>
        {
            ["SW_SERVER_MODE"] = "dev",
            ["SW_PORT"] = "7800",
            ["SW_DEV_SECRET"] = "s3cret",
            ["SW_MAX_PLAYERS"] = "8",
            ["SW_LOG_LEVEL"] = "warning",
        };

        HostOptions options = HostOptions.Parse([], name => environment.GetValueOrDefault(name));

        Assert.Equal(new HostOptions { Mode = HostMode.Server, Port = 7800, DevSecret = "s3cret", MaxPlayers = 8, LogLevel = LogLevel.Warning }, options);
    }

    [Fact]
    public void Dev_tools_are_off_unless_asked_for_and_inspect_turns_them_on()
    {
        Assert.False(HostOptions.Parse([], NoEnvironment).DevTools);
        Assert.True(HostOptions.Parse([], name => name == "SW_DEV_TOOLS" ? "1" : null).DevTools);
        Assert.True(HostOptions.Parse(["--server", "--dev-tools"], NoEnvironment).DevTools);

        HostOptions nearest = HostOptions.Parse(["--connect=127.0.0.1:7777", "--inspect=nearest"], NoEnvironment);
        HostOptions byId = HostOptions.Parse(["--inspect=42"], NoEnvironment);
        Assert.Equal((true, "nearest"), (nearest.DevTools, nearest.Inspect));
        Assert.Equal("42", byId.Inspect);
        Assert.Equal("click", HostOptions.Parse(["--inspect=click"], NoEnvironment).Inspect);
        Assert.Throws<ArgumentException>(() => HostOptions.Parse(["--inspect=closest"], NoEnvironment));
    }

    [Fact]
    public void The_world_path_comes_from_the_environment_or_an_argument()
    {
        Assert.Equal(string.Empty, HostOptions.Parse([], NoEnvironment).WorldPath);
        Assert.Equal("/data/world.db", HostOptions.Parse([], name => name == "SW_WORLD" ? "/data/world.db" : null).WorldPath);
        Assert.Equal("my.db", HostOptions.Parse(["--world=my.db"], name => name == "SW_WORLD" ? "/data/world.db" : null).WorldPath);
        Assert.Throws<ArgumentException>(() => HostOptions.Parse(["--world="], NoEnvironment));
    }

    [Fact]
    public void Bots_and_the_b2_benchmark_are_started_with_arguments()
    {
        Func<string, string?> docker = name => name == "SW_SERVER_MODE" ? "dev" : null;

        HostOptions bots = HostOptions.Parse(["--bots=16", "--connect=server:7777", "--account=3001"], docker);
        HostOptions botsFirst = HostOptions.Parse(["--connect=server:7777", "--bots=4"], NoEnvironment);
        HostOptions server = HostOptions.Parse(["--server", "--benchmark-out=/results", "--benchmark-players=8", "--warmup-ticks=30", "--measure-ticks=60"], NoEnvironment);

        Assert.Equal((HostMode.Bots, 16, "server", 7777, 3001UL), (bots.Mode, bots.Bots, bots.Address, bots.Port, bots.Account));
        Assert.Equal((HostMode.Bots, 4), (botsFirst.Mode, botsFirst.Bots));
        Assert.Equal(("/results", 8, 30, 60), (server.BenchmarkOut, server.BenchmarkPlayers, server.WarmupTicks, server.MeasureTicks));
        Assert.Equal((string.Empty, 900, 18_000), (bots.BenchmarkOut, bots.WarmupTicks, bots.MeasureTicks));
        Assert.Throws<ArgumentException>(() => HostOptions.Parse(["--bots=0"], NoEnvironment));

        HostOptions region = HostOptions.Parse(["--region", "--region-chunks=64", "--screenshot=shot.png"], NoEnvironment);
        Assert.Equal((HostMode.Region, 64, "shot.png"), (region.Mode, region.RegionChunks, region.Screenshot));
        Assert.Equal(8, HostOptions.Parse(["--region"], NoEnvironment).RegionChunks);
        HostOptions b3 = HostOptions.Parse(["--benchmark=B3", "--benchmark-out=out"], NoEnvironment);
        Assert.Equal((HostMode.Region, "B3", "out"), (b3.Mode, b3.Benchmark, b3.BenchmarkOut));
        Assert.Throws<ArgumentException>(() => HostOptions.Parse(["--benchmark=B9"], NoEnvironment));
        HostOptions showcase = HostOptions.Parse(["--showcase=predator", "--showcase-distance=12"], NoEnvironment);
        Assert.Equal((HostMode.Region, "predator", 12), (showcase.Mode, showcase.Showcase, showcase.ShowcaseDistance));
        Assert.Throws<ArgumentException>(() => HostOptions.Parse(["--showcase=dragon"], NoEnvironment));
    }

    [Fact]
    public void Arguments_win_over_the_environment()
    {
        HostOptions options = HostOptions.Parse(
            ["--connect=server:7777", "--exit-on-welcome", "--account=2001"],
            name => name == "SW_SERVER_MODE" ? "dev" : null);

        Assert.Equal(HostMode.Client, options.Mode);
        Assert.Equal("server", options.Address);
        Assert.Equal(7777, options.Port);
        Assert.True(options.ExitOnWelcome);
        Assert.Equal(2001UL, options.Account);
    }

    [Theory]
    [InlineData("--serve")]
    [InlineData("--port=-1")]
    [InlineData("--port=abc")]
    [InlineData("--connect=nohost")]
    public void Typos_and_bad_values_are_errors(string argument)
    {
        Assert.Throws<ArgumentException>(() => HostOptions.Parse([argument], NoEnvironment));
    }
}
