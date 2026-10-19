using BenchmarkDotNet.Running;
using SumonerWorld.Benchmarks.Scenarios;

namespace SumonerWorld.Benchmarks;

/// <summary>
/// Entry point.
/// Micro-benchmarks: <c>dotnet run -c Release --project tests/SumonerWorld.Benchmarks -- --filter *</c>.
/// Scenario B5 with CSV output: <c>dotnet run -c Release --project tests/SumonerWorld.Benchmarks -- b5 [output.csv]</c>.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "b5")
        {
            return B5Runner.Run(args.Length > 1 ? args[1] : "b5.csv");
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        return 0;
    }
}
