namespace SumonerWorld.Core.Diagnostics;

/// <summary>Log severity.</summary>
public enum LogLevel
{
    /// <summary>Developer detail.</summary>
    Debug,

    /// <summary>Normal operation (a player joined, the server started).</summary>
    Info,

    /// <summary>Something unexpected that the server handled (a malformed message, a rejected login).</summary>
    Warning,

    /// <summary>Something failed and needs attention.</summary>
    Error,
}

/// <summary>
/// Structured log sink (TDD §6, R35). Core and Net only see this interface; the host decides where lines go (JSON lines on the
/// dedicated server, the Godot output in the editor). The event name is a stable, searchable key such as
/// <c>session.rejected</c>.
/// </summary>
public interface ILog
{
    /// <summary>True if messages of this level are written (check before building expensive messages).</summary>
    bool IsEnabled(LogLevel level);

    /// <summary>Writes one log entry.</summary>
    void Write(LogLevel level, string eventName, string message);
}
