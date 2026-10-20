using System.Numerics;
using System.Text.Json;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.World;
using SumonerWorld.Net.Telemetry;
using SumonerWorld.Scenarios;

namespace SumonerWorld.Tests.Net;

public class PlaytestTelemetryTests
{
    [Fact]
    public void Every_line_is_json_with_time_and_type_and_the_end_counts_them()
    {
        var text = new StringWriter();
        using var telemetry = new PlaytestTelemetry(text, ShippedData.Current, startTick: 300);
        ushort mossback = (ushort)ShippedData.Current.Species.ToList().FindIndex(species => species.Id == "mossback");
        ushort savedLife = ShippedData.Current.MemoryEventTypes.First(type => type.Key == "SavedLifeOf").Id;

        telemetry.SessionStart(300, "abc123", "test-build", 14);
        telemetry.PlayerAction(330, new PlayerInteracted(1001, InteractionKind.Hit, mossback, 3));
        telemetry.EmoteShown(345, Emote.Affection, mossback, inView: true, 5.234f, savedLife, rumour: false);
        telemetry.EmoteShown(360, Emote.Curiosity, mossback, inView: false, 40f, memoryType: null);
        telemetry.SessionEnd(600);
        telemetry.SessionEnd(900);

        JsonElement[] lines = [.. text.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonDocument.Parse(line).RootElement)];
        Assert.Equal(["session_start", "player_action", "emote_shown", "memory_behaviour_shown", "emote_shown", "session_end"], lines.Select(line => line.GetProperty("type").GetString()));
        Assert.Equal(1.0, lines[1].GetProperty("t").GetDouble());
        Assert.Equal("attack", lines[1].GetProperty("action").GetString());
        Assert.Equal("mossback", lines[1].GetProperty("target_species").GetString());
        Assert.Equal("SavedLifeOf", lines[3].GetProperty("memory_type").GetString());
        Assert.Equal(5.2, lines[3].GetProperty("distance").GetDouble());
        Assert.False(lines[3].GetProperty("rumour").GetBoolean());
        Assert.False(lines[4].TryGetProperty("memory_type", out _));
        Assert.Equal(10.0, lines[5].GetProperty("duration").GetDouble());
        Assert.Equal(2, lines[5].GetProperty("emote_shown").GetInt32());
        Assert.DoesNotContain("1001", text.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Interactions_and_the_memories_they_form_reach_the_log_through_the_event_stage()
    {
        using var world = new CreatureWorld(ShippedData.Current, new CreatureWorldSettings(), seed: 3);
        var text = new StringWriter();
        using var telemetry = new PlaytestTelemetry(text, world.Data, startTick: 0);
        var scheduler = new SimulationScheduler(world.Clock, world.Events);
        scheduler.Register(new TelemetryEventSystem(world.Events, world, telemetry));
        Entity avatar = world.SpawnPlayerAvatar(1001, new Vector3(100, 0, 100));
        Entity calf = world.SpawnCreature(world.SpeciesIndex("mossback"), new Vector3(102, 0, 100), new Personality(0, 0, 0, 0), 0);
        world.Snare(calf, 0);

        var help = new HelpTransaction(world, new CooldownTracker(), avatar, world.World.GetComponent<StableId>(calf));
        Assert.True(help.Validate(0).IsApplied);
        help.Commit(0);
        scheduler.RunTick();

        string log = text.ToString();
        Assert.Contains("\"type\":\"player_action\",\"action\":\"help\",\"target_species\":\"mossback\"", log, StringComparison.Ordinal);
        Assert.Contains("\"event_type\":\"SavedLifeOf\",\"salience\":900,\"core\":true", log, StringComparison.Ordinal);
    }
}
