using System.Globalization;
using System.Text;

namespace SumonerWorld.Net.Client;

/// <summary>
/// A player's settings (M2-17): sound volume, mouse speed, text size and whether the key hints show. Kept per account on the
/// player's computer as <c>key=value</c> lines, so children sharing a computer each keep their own; anything unknown or out of
/// range falls back to the default or the nearest allowed value.
/// </summary>
public sealed record PlayerSettings
{
    /// <summary>Lowest mouse speed, in percent.</summary>
    public const int MinMouseSpeed = 25;

    /// <summary>Highest mouse speed, in percent.</summary>
    public const int MaxMouseSpeed = 300;

    /// <summary>Volume at which the sound is off, in decibels (Godot's quietest bus volume).</summary>
    public const float SilentDb = -80f;

    /// <summary>The text sizes on offer, in percent.</summary>
    public static IReadOnlyList<int> TextSizes { get; } = [100, 125, 150];

    /// <summary>Sound volume, 0–100.</summary>
    public int Volume { get; init; } = 80;

    /// <summary>How fast the mouse turns the camera, in percent of the normal speed.</summary>
    public int MouseSpeed { get; init; } = 100;

    /// <summary>Size of all windows and text, in percent (one of <see cref="TextSizes"/>).</summary>
    public int TextSize { get; init; } = 100;

    /// <summary>The key hints next to the bars and the companions.</summary>
    public bool ShowHints { get; init; } = true;

    /// <summary>The player has seen the key list once (it opens by itself the first time).</summary>
    public bool SeenKeys { get; init; }

    /// <summary>The volume as a bus volume in decibels; <see cref="SilentDb"/> at 0.</summary>
    public float VolumeDb => Volume <= 0 ? SilentDb : MathF.Max(SilentDb, 20f * MathF.Log10(Volume / 100f));

    /// <summary>The text size as a scale factor.</summary>
    public float TextScale => TextSize / 100f;

    /// <summary>The mouse speed as a factor.</summary>
    public float MouseFactor => MouseSpeed / 100f;

    /// <summary>The same settings brought into range.</summary>
    public PlayerSettings Clamped() => this with
    {
        Volume = Math.Clamp(Volume, 0, 100),
        MouseSpeed = Math.Clamp(MouseSpeed, MinMouseSpeed, MaxMouseSpeed),
        TextSize = NearestTextSize(TextSize),
    };

    /// <summary>The next text size, back to the first after the last.</summary>
    public PlayerSettings NextTextSize()
    {
        int index = -1;
        for (int i = 0; i < TextSizes.Count; i++)
        {
            index = TextSizes[i] == TextSize ? i : index;
        }

        return this with { TextSize = TextSizes[(index + 1) % TextSizes.Count] };
    }

    /// <summary>Reads settings written by <see cref="ToString"/>; missing, unknown or broken lines keep the defaults.</summary>
    public static PlayerSettings Parse(string? text)
    {
        var settings = new PlayerSettings();
        foreach (string line in (text ?? string.Empty).Split('\n'))
        {
            int equals = line.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0)
            {
                continue;
            }

            string value = line[(equals + 1)..].Trim();
            bool number = int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int parsed);
            bool flag = bool.TryParse(value, out bool yes);
            settings = line[..equals].Trim() switch
            {
                "volume" when number => settings with { Volume = parsed },
                "mouse" when number => settings with { MouseSpeed = parsed },
                "text" when number => settings with { TextSize = parsed },
                "hints" when flag => settings with { ShowHints = yes },
                "seenKeys" when flag => settings with { SeenKeys = yes },
                _ => settings,
            };
        }

        return settings.Clamped();
    }

    /// <inheritdoc />
    public override string ToString()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"volume={Volume}\n");
        text.Append(CultureInfo.InvariantCulture, $"mouse={MouseSpeed}\n");
        text.Append(CultureInfo.InvariantCulture, $"text={TextSize}\n");
        text.Append(CultureInfo.InvariantCulture, $"hints={(ShowHints ? "true" : "false")}\n");
        text.Append(CultureInfo.InvariantCulture, $"seenKeys={(SeenKeys ? "true" : "false")}\n");
        return text.ToString();
    }

    private static int NearestTextSize(int size)
    {
        int best = TextSizes[0];
        foreach (int candidate in TextSizes)
        {
            best = Math.Abs(candidate - size) < Math.Abs(best - size) ? candidate : best;
        }

        return best;
    }
}
