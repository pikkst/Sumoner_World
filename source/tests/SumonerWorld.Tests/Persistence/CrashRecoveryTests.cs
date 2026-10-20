using System.Diagnostics;
using System.Globalization;
using SumonerWorld.Core.Intents;
using SumonerWorld.CrashHarness;
using SumonerWorld.Persistence;
using SumonerWorld.Persistence.Backups;

namespace SumonerWorld.Tests.Persistence;

/// <summary>
/// Spike 3 (TDD §8): the trade process is killed three times during 1,000 random trades and restarted each time, resending the last
/// few trades the way clients resend unanswered intents. Afterwards the database must equal applying every trade exactly once: no
/// item lost, none duplicated, no trade applied twice.
/// </summary>
public sealed class CrashRecoveryTests : IDisposable
{
    private const ulong Seed = 0x7EADE5;
    private const uint Trades = 1000;
    private const uint Resend = 5;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sw-crash-" + Guid.NewGuid().ToString("N"));

    public CrashRecoveryTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Killing_the_process_during_1000_trades_loses_and_duplicates_nothing()
    {
        string database = Path.Combine(_directory, "world.db");
        uint seen = 0;
        foreach (uint killAt in new uint[] { 150, 450, 750 })
        {
            seen = RunUntilKilled(database, from: seen > Resend ? seen - Resend : 1, killAt);
            Assert.True(seen >= killAt, $"The run reached trade {seen} before it was killed.");
        }

        (uint last, int duplicates) = RunToEnd(database, from: seen - Resend);

        Assert.Equal(Trades, last);
        AssertEqualsSingleReplay(database);
        Assert.True(duplicates > (int)Resend, $"The resent trades were answered from the dedupe table ({duplicates}).");
    }

    private static void AssertEqualsSingleReplay(string database)
    {
        var expected = new MemoryLedger();
        TradeScript.Seed(expected);
        var results = new IntentResult[Trades + 1];
        for (uint trade = 1; trade <= Trades; trade++)
        {
            results[trade] = TradeScript.Apply(expected, Seed, trade);
        }

        using WorldStore store = WorldStore.Open(database, new WorldBackups(Path.Combine(Path.GetDirectoryName(database)!, "backups")));
        var items = store.ReadItems();
        Assert.Equal(TradeScript.Items, items.Count);
        Assert.Equal(expected.Items, items);
        Assert.Equal(expected.GetMeta(TradeScript.NextItemKey), store.GetMeta(TradeScript.NextItemKey));
        for (uint trade = 1; trade <= Trades; trade++)
        {
            Assert.True(store.TryGetIntent(new IntentId(1, trade), out IntentResult result), $"Trade {trade} has a dedupe record.");
            Assert.Equal(results[trade], result);
        }
    }

    private static uint RunUntilKilled(string database, uint from, uint killAt)
    {
        using Process process = Start(database, from);
        uint last = 0;
        while (process.StandardOutput.ReadLine() is string line)
        {
            if (TryParse(line, "committed ", out uint trade))
            {
                last = trade;
                if (trade >= killAt)
                {
                    process.Kill(entireProcessTree: true);
                    break;
                }
            }
        }

        process.WaitForExit();
        return last;
    }

    private static (uint Last, int Duplicates) RunToEnd(string database, uint from)
    {
        using Process process = Start(database, from);
        uint last = 0;
        int duplicates = -1;
        while (process.StandardOutput.ReadLine() is string line)
        {
            if (TryParse(line, "committed ", out uint trade))
            {
                last = trade;
            }
            else if (TryParse(line, "duplicates ", out uint count))
            {
                duplicates = (int)count;
            }
        }

        process.WaitForExit();
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
        return (last, duplicates);
    }

    private static Process Start(string database, uint from)
    {
        string harness = Path.Combine(
            AppContext.BaseDirectory.Replace(
                Path.Combine("SumonerWorld.Tests", "bin"), Path.Combine("SumonerWorld.CrashHarness", "bin"), StringComparison.Ordinal),
            "SumonerWorld.CrashHarness.dll");
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in new[] { harness, "trades", database, Seed.ToString(CultureInfo.InvariantCulture), from.ToString(CultureInfo.InvariantCulture), Trades.ToString(CultureInfo.InvariantCulture) })
        {
            start.ArgumentList.Add(argument);
        }

        return Process.Start(start) ?? throw new InvalidOperationException("Could not start the crash harness.");
    }

    private static bool TryParse(string line, string prefix, out uint value)
    {
        value = 0;
        return line.StartsWith(prefix, StringComparison.Ordinal)
            && uint.TryParse(line.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
