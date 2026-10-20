using Microsoft.Data.Sqlite;
using SumonerWorld.Core.Intents;
using SumonerWorld.Persistence;
using SumonerWorld.Persistence.Backups;
using SumonerWorld.Persistence.Records;
using SumonerWorld.Persistence.Schema;

namespace SumonerWorld.Tests.Persistence;

public sealed class WorldStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sw-store-" + Guid.NewGuid().ToString("N"));

    public WorldStoreTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void A_new_world_is_created_at_the_latest_schema_and_committed_changes_survive_reopening()
    {
        using (WorldStore store = Open())
        {
            Assert.Equal(Migrations.Latest, store.SchemaVersion);
            using IWorldTransaction transaction = store.Begin();
            transaction.PutItem(5, 1001, new ItemRecord { Kind = 2, Quantity = 3 });
            transaction.RecordIntent(new IntentId(9, 1), IntentResult.Applied(4), 77);
            transaction.SetMeta("world_tick", 77);
            transaction.Commit();
        }

        using WorldStore reopened = Open();
        Assert.True(reopened.TryGetItem(5, out ulong owner, out ItemRecord item));
        Assert.Equal((1001UL, new ItemRecord { Kind = 2, Quantity = 3 }), (owner, item));
        Assert.True(reopened.TryGetIntent(new IntentId(9, 1), out IntentResult result));
        Assert.Equal(IntentResult.Applied(4), result);
        Assert.Equal(77, reopened.GetMeta("world_tick"));
    }

    [Fact]
    public void A_transaction_disposed_without_commit_changes_nothing()
    {
        using WorldStore store = Open();
        using (IWorldTransaction transaction = store.Begin())
        {
            transaction.PutItem(1, 1, new ItemRecord());
            transaction.RecordIntent(new IntentId(1, 1), IntentResult.Applied(), 0);
            Assert.True(transaction.TryGetItem(1, out _, out _), "A transaction sees its own writes.");
        }

        Assert.False(store.TryGetItem(1, out _, out _));
        Assert.False(store.TryGetIntent(new IntentId(1, 1), out _));
    }

    [Fact]
    public void The_dedupe_table_keeps_the_last_1024_ids_per_session_and_refuses_a_repeat()
    {
        using WorldStore store = Open();
        using (IWorldTransaction transaction = store.Begin())
        {
            for (uint sequence = 1; sequence <= 1100; sequence++)
            {
                transaction.RecordIntent(new IntentId(1, sequence), IntentResult.Applied(), sequence);
            }

            transaction.RecordIntent(new IntentId(2, 1), IntentResult.Applied(), 0);
            transaction.Commit();
        }

        Assert.False(store.TryGetIntent(new IntentId(1, 76), out _));
        Assert.True(store.TryGetIntent(new IntentId(1, 77), out _));
        Assert.True(store.TryGetIntent(new IntentId(2, 1), out _));
        using IWorldTransaction again = store.Begin();
        Assert.Throws<SqliteException>(() => again.RecordIntent(new IntentId(1, 1100), IntentResult.Applied(), 0));
    }

    [Fact]
    public void A_world_from_a_newer_server_is_refused()
    {
        using (WorldStore store = Open())
        {
            using IWorldTransaction transaction = store.Begin();
            transaction.SetMeta(WorldStore.SchemaVersionKey, Migrations.Latest + 1);
            transaction.Commit();
        }

        var error = Assert.Throws<StoreVersionException>(() => Open());
        Assert.Contains("newer server", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_fixture_save_loads_and_migrates_to_the_latest_schema()
    {
        string[] fixtures = Directory.GetFiles(FixtureDirectory(), "world-v*.db");
        Assert.NotEmpty(fixtures);
        foreach (string fixture in fixtures)
        {
            string copy = Path.Combine(_directory, Path.GetFileName(fixture));
            File.Copy(fixture, copy);
            using WorldStore store = WorldStore.Open(copy, new WorldBackups(Path.Combine(_directory, "backups")));

            Assert.Equal(Migrations.Latest, store.SchemaVersion);
            Assert.Equal(3, store.ReadItems().Count);
            Assert.True(store.TryGetIntent(new IntentId(7, 42), out _));
        }
    }

    [Fact]
    public void Backups_are_consistent_copies_and_only_the_newest_are_kept()
    {
        var clock = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
        var backups = new WorldBackups(Path.Combine(_directory, "backups"), keep: 3, () => clock = clock.AddMinutes(30));
        using WorldStore store = WorldStore.Open(Path.Combine(_directory, "world.db"), backups);
        using (IWorldTransaction transaction = store.Begin())
        {
            transaction.PutItem(1, 1001, new ItemRecord { Kind = 1 });
            transaction.Commit();
        }

        string first = store.Backup("timed");
        for (int i = 0; i < 4; i++)
        {
            store.Backup("timed");
        }

        Assert.Equal(3, backups.List().Count);
        Assert.DoesNotContain(first, backups.List());
        using WorldStore copy = WorldStore.Open(backups.List()[0], new WorldBackups(Path.Combine(_directory, "unused")));
        Assert.True(copy.TryGetItem(1, out ulong owner, out _));
        Assert.Equal(1001UL, owner);
        Assert.Throws<ArgumentException>(() => store.Backup("Not Allowed"));
    }

    [Fact]
    public void Record_payloads_carry_a_version_and_damaged_ones_are_reported()
    {
        var codec = new MessagePackRecordCodec<ItemRecord>(1);
        byte[] payload = codec.Encode(new ItemRecord { Kind = 9, Quantity = 64 });

        Assert.Equal(1, payload[0]);
        Assert.Equal(new ItemRecord { Kind = 9, Quantity = 64 }, codec.Decode(payload));
        Assert.Throws<RecordFormatException>(() => new MessagePackRecordCodec<ItemRecord>(2).Decode(payload));
        Assert.Throws<RecordFormatException>(() => codec.Decode([1, 0xC1, 0xFF]));
        Assert.Throws<RecordFormatException>(() => codec.Decode([]));
    }

    private static string FixtureDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "tests", "fixtures", "saves");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("tests/fixtures/saves not found above the test output.");
    }

    private WorldStore Open() => WorldStore.Open(Path.Combine(_directory, "world.db"), new WorldBackups(Path.Combine(_directory, "backups")));
}
