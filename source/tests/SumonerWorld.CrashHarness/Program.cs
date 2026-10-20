using System.Globalization;
using System.Numerics;
using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Cognition.Opinions;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.World;
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
    transaction.SaveWorld(FixtureWorld(), [new PlayerSave(1001, new Vector3(250, 0, 260))]);
    transaction.Commit();
    Console.WriteLine($"fixture v{store.SchemaVersion} written to {path}");
    return 0;
}

static WorldSnapshot FixtureWorld()
{
    var memory = new MemoryRecord(5, new MemorySubject(SubjectKind.Player, 1001), 40, 600, 120, 200, 2, MemoryTags.None);
    var mind = new MindState(
        new Personality(10, -20, 30, -40),
        new Personality(12, -20, 30, -38),
        [],
        [memory],
        [new OpinionBaseline(new MemorySubject(SubjectKind.Player, 1001), new Opinion(150, -20, 80, 0))],
        [new NeedState(1000, 100, 200), new NeedState(0, 100, 300), new NeedState(0, 0, 50), new NeedState(5000, 90, 100), new NeedState(0, 0, 10)]);
    var creature = new CreatureSave(new StableId(17), "mossback", new Vector3(255, 0, 255), Vector3.UnitX, 3, mind, default(BondState));
    return new WorldSnapshot(300, 0xDEADBEEF, 17, [creature], [new ReputationSave("mossback", 0, 1001, -40)]);
}
