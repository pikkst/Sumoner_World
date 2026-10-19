using Godot;
using SumonerWorld.Core.Data;

namespace SumonerWorld.Game.Data;

/// <summary>Loads the game data (<c>res://data/**/*.json</c>) through Godot's file API, so it also works from an exported pack.</summary>
public static class GameData
{
    /// <summary>The data root.</summary>
    public const string Root = "res://data";

    /// <summary>Reads and validates every data file (throws <see cref="DataValidationException"/> listing every problem).</summary>
    public static DataRegistry Load()
    {
        var files = new List<DataFile>();
        Collect(Root, string.Empty, files);
        return DataRegistryLoader.Load(files.OrderBy(file => file.Path, StringComparer.Ordinal));
    }

    private static void Collect(string directory, string relative, List<DataFile> files)
    {
        using DirAccess access = DirAccess.Open(directory) ?? throw new DirectoryNotFoundException($"{directory} is missing.");
        foreach (string child in access.GetDirectories())
        {
            Collect($"{directory}/{child}", $"{relative}{child}/", files);
        }

        foreach (string name in access.GetFiles())
        {
            if (name.EndsWith(".json", StringComparison.Ordinal))
            {
                files.Add(new DataFile(relative + name, Godot.FileAccess.GetFileAsString($"{directory}/{name}")));
            }
        }
    }
}
