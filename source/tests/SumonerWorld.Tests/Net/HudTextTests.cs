using SumonerWorld.Net.Client;

namespace SumonerWorld.Tests.Net;

public class HudTextTests
{
    private static readonly string[] Keys = ["W A S D", "Left click", "Shift", "Space", "T or middle click", "E", "F", "R", "P", "Q", "H", "Z", "X", "K", "1 to 0", "L", "I", "B", "J", "F1", "Esc"];
    private static readonly int[] Steps = [1, 2, 3];

    [Fact]
    public void Settings_round_trip_and_start_from_friendly_defaults()
    {
        var settings = new PlayerSettings();
        Assert.Equal((80, 100, 100, true, false), (settings.Volume, settings.MouseSpeed, settings.TextSize, settings.ShowHints, settings.SeenKeys));

        var changed = new PlayerSettings { Volume = 35, MouseSpeed = 150, TextSize = 125, ShowHints = false, SeenKeys = true };

        Assert.Equal(changed, PlayerSettings.Parse(changed.ToString()));
    }

    [Fact]
    public void Broken_unknown_or_out_of_range_settings_fall_back()
    {
        PlayerSettings settings = PlayerSettings.Parse("volume=250\nmouse=2\ntext=140\nhints=maybe\ncolour=blue\nnonsense\n=5\r\n");

        Assert.Equal(100, settings.Volume);
        Assert.Equal(PlayerSettings.MinMouseSpeed, settings.MouseSpeed);
        Assert.Equal(150, settings.TextSize);
        Assert.True(settings.ShowHints);
        Assert.Equal(new PlayerSettings(), PlayerSettings.Parse(null));
        Assert.Equal(new PlayerSettings(), PlayerSettings.Parse("volume=loud"));
    }

    [Fact]
    public void Volume_turns_into_decibels_and_text_size_cycles()
    {
        Assert.Equal(0f, new PlayerSettings { Volume = 100 }.VolumeDb, 3);
        Assert.Equal(-6.02f, new PlayerSettings { Volume = 50 }.VolumeDb, 2);
        Assert.Equal(PlayerSettings.SilentDb, new PlayerSettings { Volume = 0 }.VolumeDb);
        Assert.Equal(1.5f, new PlayerSettings { MouseSpeed = 150 }.MouseFactor);

        var settings = new PlayerSettings();
        Assert.Equal([125, 150, 100], Steps.Select(steps => Enumerable.Range(0, steps).Aggregate(settings, (s, _) => s.NextTextSize()).TextSize));
        Assert.Equal(1.25f, settings.NextTextSize().TextScale);
    }

    [Fact]
    public void The_key_list_names_every_key_the_game_listens_to()
    {
        string list = HudText.KeyListText();

        foreach (string key in Keys)
        {
            Assert.Contains($"  {key}: ", list, StringComparison.Ordinal);
        }

        Assert.Equal(HudText.KeyList.Count, list.Split('\n').Count(line => !line.StartsWith(' ')));
    }

    [Fact]
    public void Low_vitals_are_named_health_first_and_not_while_down()
    {
        Assert.StartsWith("Low health", HudText.Warning(10, 10, 10, down: false), StringComparison.Ordinal);
        Assert.StartsWith("Tired", HudText.Warning(200, 10, 10, down: false), StringComparison.Ordinal);
        Assert.StartsWith("Low mana", HudText.Warning(200, 200, 10, down: false), StringComparison.Ordinal);
        Assert.Empty(HudText.Warning(200, 200, 200, down: false));
        Assert.Empty(HudText.Warning(0, 0, 0, down: true));
    }
}
