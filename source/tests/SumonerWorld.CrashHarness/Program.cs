using System.Globalization;
using SumonerWorld.Core.Intents;
using SumonerWorld.CrashHarness;
using SumonerWorld.Persistence;
using SumonerWorld.Persistence.Backups;
using SumonerWorld.Persistence.Records;

// Usage:
//   trades <db> <seed> <from> <to>   run trades from..to (resent ones are answered by the dedupe table); prints "committed k" per commit
//   fixture <db>                      write the fixture save for the current schema version (tests/fixtures/saves)
const string SeededKey = "seeded";
const ulong Session = 1;

switch (args.FirstOrDefault())
{
    case "trades" when args.Length == 5:
        return RunTrades(args[1], ulong.Parse(args[2], CultureInfo.InvariantCulture), uint.Parse(args[3], CultureInfo.InvariantCulture), uint.Parse(args[4], CultureInfo.InvariantCulture));
    case "fixture" when args.Length == 2:
        return WriteFixture(args[1]);
    default:
        Console.Error.WriteLine("usage: trades <db> <seed> <from> <to> | fixture <db>");
        return 64;
}

static WorldStore Open(string path) =>
    WorldStore.Open(path, new WorldBackups(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "backups")));

static int RunTrades(string path, ulong seed, uint from, uint to)
{
    using WorldStore store = Open(path);
    if (store.GetMeta(SeededKey) == 0)
    {
        using IWorldTransaction seeding = store.Begin();
        TradeScript.Seed(new TransactionLedger(seeding));
        seeding.SetMeta(SeededKey, 1);
        seeding.Commit();
    }

    int duplicates = 0;
    for (uint trade = from; trade <= to; trade++)
    {
        using IWorldTransaction transaction = store.Begin();
        var id = new IntentId(Session, trade);
        if (transaction.TryGetIntent(id, out _))
        {
            duplicates++;
            continue;
        }

        IntentResult result = TradeScript.Apply(new TransactionLedger(transaction), seed, trade);
        transaction.RecordIntent(id, result, trade);
        transaction.Commit();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"committed {trade}"));
    }

    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"duplicates {duplicates}"));
    return 0;
}

static int WriteFixture(string path)
{
    File.Delete(path);
    using WorldStore store = Open(path);
    using IWorldTransaction transaction = store.Begin();
    transaction.PutItem(1, 1001, new ItemRecord { Kind = 3, Quantity = 1 });
    transaction.PutItem(2, 1001, new ItemRecord { Kind = 4, Quantity = 20 });
    transaction.PutItem(3, 1002, new ItemRecord { Kind = 0, Quantity = 1 });
    transaction.RecordIntent(new IntentId(7, 42), IntentResult.Applied(1), 300);
    transaction.SetMeta("world_tick", 300);
    transaction.Commit();
    Console.WriteLine($"fixture v{store.SchemaVersion} written to {path}");
    return 0;
}
