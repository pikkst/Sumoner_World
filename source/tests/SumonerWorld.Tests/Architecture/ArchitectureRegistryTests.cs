using System.Reflection;
using SumonerWorld.Core.Randomness;

namespace SumonerWorld.Tests.Architecture;

/// <summary>Layer-D enforcement of the system registry (R47): every public Core interface must be registered.</summary>
public class ArchitectureRegistryTests
{
    private const string RegistryRelativePath = "documentation/technical/architecture/system_registry.md";

    private static readonly Assembly CoreAssembly = typeof(IRandom).Assembly;

    [Fact]
    public void Every_public_core_interface_is_listed_in_the_system_registry()
    {
        var registry = File.ReadAllText(FindRegistry());

        var unregistered = CoreAssembly.GetExportedTypes()
            .Where(type => type.IsInterface)
            .Select(type => type.Name)
            .Where(name => !registry.Contains($"`{name}`", StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            unregistered.Length == 0,
            $"Add these interfaces to {RegistryRelativePath} before merging (R47): {string.Join(", ", unregistered)}");
    }

    private static string FindRegistry()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, RegistryRelativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Could not find {RegistryRelativePath} above {AppContext.BaseDirectory}.");
    }
}
