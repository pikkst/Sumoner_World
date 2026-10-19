using System.Text.Json;
using Godot;
using SumonerWorld.Core.Diagnostics;

namespace SumonerWorld.Game.Diagnostics;

/// <summary>
/// <see cref="ILog"/> for the Godot hosts (TDD §6): one JSON object per line on standard output, which Docker collects as the
/// server log. Lines below <see cref="MinimumLevel"/> are skipped.
/// </summary>
public sealed class GodotLog(LogLevel minimumLevel) : ILog
{
    /// <summary>Lowest level written.</summary>
    public LogLevel MinimumLevel { get; } = minimumLevel;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel level) => level >= MinimumLevel;

    /// <inheritdoc />
    public void Write(LogLevel level, string eventName, string message)
    {
        if (!IsEnabled(level))
        {
            return;
        }

        string line = JsonSerializer.Serialize(new LogLine(DateTime.UtcNow.ToString("O"), level.ToString(), eventName, message));
        if (level >= LogLevel.Warning)
        {
            GD.PrintErr(line);
        }
        else
        {
            GD.Print(line);
        }
    }

    private sealed record LogLine(string Time, string Level, string Event, string Message);
}
