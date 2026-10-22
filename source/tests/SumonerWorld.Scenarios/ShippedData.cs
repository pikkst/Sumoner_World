using SumonerWorld.Core.Data;

namespace SumonerWorld.Scenarios;

/// <summary>
/// The game data shipped in the Godot project (<c>source/project-sumoner-worl/data</c>), loaded once, so scenarios run with the
/// real tuning values. Scenarios are test tooling and run from inside the repository.
/// </summary>
public static class ShippedData
{
    private const string RelativePath = "source/project-sumoner-worl/data";

    private static readonly Lazy<IReadOnlyList<DataFile>> AllFiles = new(Read);
    private static readonly Lazy<DataRegistry> Registry = new(() => DataRegistryLoader.Load(AllFiles.Value));

    /// <summary>The validated registry.</summary>
    public static DataRegistry Current => Registry.Value;

    /// <summary>The data files as read (for tests that change one and load again).</summary>
    public static IReadOnlyList<DataFile> Files => AllFiles.Value;

    private static List<DataFile> Read()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string root = Path.Combine(directory.FullName, RelativePath);
            if (Directory.Exists(root))
            {
                return [.. Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
                    .Order(StringComparer.Ordinal)
                    .Select(path => new DataFile(Path.GetRelativePath(root, path), File.ReadAllText(path)))];
            }
        }

        throw new DirectoryNotFoundException($"Could not find {RelativePath} above {AppContext.BaseDirectory}; run scenarios from the repository.");
    }
}
