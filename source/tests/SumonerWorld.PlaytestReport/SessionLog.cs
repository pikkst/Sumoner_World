using System.Text.Json;

namespace SumonerWorld.PlaytestReport;

/// <summary>A creature showing, to the tester, an emote driven by a memory of them (telemetry <c>memory_behaviour_shown</c>).</summary>
public sealed record MemoryMoment(double Time, string MemoryType, string Emote, bool InView, bool Rumour);

/// <summary>What one session's telemetry file says (QA plan B6), read back for the report.</summary>
public sealed record SessionLog(string Session, double Duration, IReadOnlyDictionary<string, int> Actions, IReadOnlyList<MemoryMoment> Moments)
{
    /// <summary>The noticing window (QA plan B5): a reaction counts only if it happened at or before 10:00 of play.</summary>
    public const double WindowSeconds = 600;

    /// <summary>The first memory moment on the tester's screen inside the window, or null when the build never staged one in view.</summary>
    public MemoryMoment? FirstInView => Moments.FirstOrDefault(moment => moment.InView && moment.Time <= WindowSeconds);

    /// <summary>Reads a JSON-lines telemetry file. Unknown line types are skipped, so newer builds stay readable.</summary>
    public static SessionLog Read(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        string session = string.Empty;
        double duration = 0;
        var actions = new Dictionary<string, int>(StringComparer.Ordinal);
        var moments = new List<MemoryMoment>();
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            double t = root.GetProperty("t").GetDouble();
            duration = Math.Max(duration, t);
            switch (root.GetProperty("type").GetString())
            {
                case "session_start":
                    session = root.GetProperty("session").GetString() ?? string.Empty;
                    break;
                case "player_action":
                    string action = root.GetProperty("action").GetString() ?? "unknown";
                    actions[action] = actions.GetValueOrDefault(action) + 1;
                    break;
                case "memory_behaviour_shown":
                    moments.Add(new MemoryMoment(
                        t,
                        root.GetProperty("memory_type").GetString() ?? string.Empty,
                        root.GetProperty("emote").GetString() ?? string.Empty,
                        root.GetProperty("in_view").GetBoolean(),
                        root.TryGetProperty("rumour", out JsonElement rumour) && rumour.GetBoolean()));
                    break;
                case "session_end":
                    duration = root.GetProperty("duration").GetDouble();
                    break;
                default:
                    break;
            }
        }

        return new SessionLog(session, duration, actions, moments);
    }
}
