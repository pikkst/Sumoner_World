using System.Reflection;
using SumonerWorld.Core.Randomness;

namespace SumonerWorld.Tests.Architecture;

/// <summary>Layer-D enforcement from development_rules_enforcement.md (R02, R11, R12).</summary>
public class LayeringTests
{
    private static readonly Assembly CoreAssembly = typeof(IRandom).Assembly;

    [Fact]
    public void Core_references_only_the_base_class_library()
    {
        string[] allowedPrefixes = ["System", "Microsoft.Win32", "netstandard", "mscorlib"];

        var offending = CoreAssembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => !allowedPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToArray();

        Assert.True(
            offending.Length == 0,
            $"SumonerWorld.Core must stay engine-agnostic (ADR-001, R12). Unexpected references: {string.Join(", ", offending)}");
    }

    [Fact]
    public void Net_references_only_core_and_the_base_class_library()
    {
        string[] allowedPrefixes = ["System", "Microsoft.Win32", "netstandard", "mscorlib", "SumonerWorld.Core"];

        var offending = typeof(SumonerWorld.Net.Transport.ITransport).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => !allowedPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToArray();

        Assert.True(offending.Length == 0, $"SumonerWorld.Net must not depend on Godot or other layers (ADR-001). Unexpected: {string.Join(", ", offending)}");
    }

    [Fact]
    public void Core_does_not_reference_higher_layers()
    {
        Assert.DoesNotContain(CoreAssembly.GetReferencedAssemblies(), reference => reference.Name == "SumonerWorld.Net");
    }

    [Fact]
    public void Core_types_live_in_the_core_namespace()
    {
        var misplaced = CoreAssembly.GetTypes()
            .Where(type => type.Namespace is not null && !type.Namespace.StartsWith("SumonerWorld.Core", StringComparison.Ordinal))
            .Select(type => type.FullName)
            .ToArray();

        Assert.Empty(misplaced);
    }
}
