namespace SumonerWorld.Core.Data;

/// <summary>
/// One data file's text, read by the host (Godot <c>res://data</c> or a test). Core never touches the file system (R12).
/// </summary>
/// <param name="Path">Path used in error messages, e.g. <c>data/ai/species/mossback.json</c>.</param>
/// <param name="Content">The JSON text.</param>
public readonly record struct DataFile(string Path, string Content);
