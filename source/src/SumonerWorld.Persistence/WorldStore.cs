using Microsoft.Data.Sqlite;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.World;
using SumonerWorld.Persistence.Backups;
using SumonerWorld.Persistence.Records;
using SumonerWorld.Persistence.Schema;

namespace SumonerWorld.Persistence;

/// <summary>The save was written by a newer server (TDD §4.3: never downgrade).</summary>
public sealed class StoreVersionException : Exception
{
    /// <summary>Creates the exception.</summary>
    public StoreVersionException()
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    public StoreVersionException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and cause.</summary>
    public StoreVersionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The world database (TDD §4.1–4.2): one SQLite file per world. Every change goes through an <see cref="IWorldTransaction"/>, which
/// commits atomically; a transaction's effect and its intent dedupe record are written together, so a crash and resend can never
/// apply an action twice (TDD §3.4).
/// </summary>
public interface IWorldStore : IDisposable
{
    /// <summary>Schema version of the open database (always the latest after opening).</summary>
    int SchemaVersion { get; }

    /// <summary>Starts a transaction; dispose without <see cref="IWorldTransaction.Commit"/> to roll back.</summary>
    IWorldTransaction Begin();

    /// <summary>Reads a meta value (0 when unset).</summary>
    long GetMeta(string key);

    /// <summary>Reads an item.</summary>
    bool TryGetItem(ulong id, out ulong owner, out ItemRecord item);

    /// <summary>All items as (id, owner, record), by id.</summary>
    IReadOnlyList<(ulong Id, ulong Owner, ItemRecord Item)> ReadItems();

    /// <summary>The recorded result of an intent, if it was processed.</summary>
    bool TryGetIntent(IntentId id, out IntentResult result);

    /// <summary>Makes a backup now; returns its path.</summary>
    string Backup(string reason);

    /// <summary>The last saved world, or null for a world that was never saved.</summary>
    SavedWorld? LoadWorld();
}

/// <summary>One atomic unit of change. Reads see the transaction's own writes.</summary>
public interface IWorldTransaction : IDisposable
{
    /// <summary>Reads an item.</summary>
    bool TryGetItem(ulong id, out ulong owner, out ItemRecord item);

    /// <summary>Ids of the items an owner has, ascending.</summary>
    IReadOnlyList<ulong> ItemsOwnedBy(ulong owner);

    /// <summary>Inserts or replaces an item.</summary>
    void PutItem(ulong id, ulong owner, ItemRecord item);

    /// <summary>Removes an item.</summary>
    void DeleteItem(ulong id);

    /// <summary>The recorded result of an intent, if it was processed.</summary>
    bool TryGetIntent(IntentId id, out IntentResult result);

    /// <summary>Records an intent's result (and drops that session's ids older than the dedupe window).</summary>
    void RecordIntent(IntentId id, IntentResult result, ulong tick);

    /// <summary>Reads a meta value (0 when unset).</summary>
    long GetMeta(string key);

    /// <summary>Sets a meta value.</summary>
    void SetMeta(string key, long value);

    /// <summary>Replaces the saved world (creatures, players, reputation, clock, random state, id counter) with this one.</summary>
    void SaveWorld(WorldSnapshot world, IReadOnlyList<PlayerSave> players);

    /// <summary>Commits everything at once.</summary>
    void Commit();
}

/// <summary>SQLite world store: WAL journal with <c>synchronous=FULL</c>, so a committed transaction survives a process kill or power loss.</summary>
public sealed class WorldStore : IWorldStore
{
    /// <summary>Meta key of the schema version.</summary>
    public const string SchemaVersionKey = "schema_version";

    private readonly SqliteConnection _connection;
    private readonly IBackupService _backups;
    private readonly MessagePackRecordCodec<ItemRecord> _items = new(1);
    private readonly WorldTables _world;

    private WorldStore(SqliteConnection connection, IBackupService backups)
    {
        _connection = connection;
        _backups = backups;
        _world = new WorldTables(this);
    }

    /// <inheritdoc />
    public int SchemaVersion { get; private set; }

    /// <summary>
    /// Opens (or creates) a world database and migrates it to the latest schema, after a backup when an existing world is migrated.
    /// A database from a newer server is refused with <see cref="StoreVersionException"/>.
    /// </summary>
    public static WorldStore Open(string path, IBackupService backups)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(backups);

        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        var store = new WorldStore(connection, backups);
        try
        {
            connection.Open();
            store.Execute("PRAGMA journal_mode=WAL;");
            store.Execute("PRAGMA synchronous=FULL;");
            store.Migrate();
            return store;
        }
        catch
        {
            store.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public IWorldTransaction Begin() => new WorldTransaction(this, _connection.BeginTransaction());

    /// <inheritdoc />
    public long GetMeta(string key) => ReadMeta(key, null);

    /// <inheritdoc />
    public bool TryGetItem(ulong id, out ulong owner, out ItemRecord item) => ReadItem(id, null, out owner, out item);

    /// <inheritdoc />
    public IReadOnlyList<(ulong Id, ulong Owner, ItemRecord Item)> ReadItems()
    {
        using SqliteCommand command = Command("SELECT id, owner, payload FROM items ORDER BY id;", null);
        using SqliteDataReader reader = command.ExecuteReader();
        var items = new List<(ulong, ulong, ItemRecord)>();
        while (reader.Read())
        {
            items.Add(((ulong)reader.GetInt64(0), (ulong)reader.GetInt64(1), _items.Decode((byte[])reader.GetValue(2))));
        }

        return items;
    }

    /// <inheritdoc />
    public bool TryGetIntent(IntentId id, out IntentResult result) => ReadIntent(id, null, out result);

    /// <inheritdoc />
    public string Backup(string reason) => _backups.Create(_connection, reason);

    /// <inheritdoc />
    public SavedWorld? LoadWorld() => _world.Load();

    /// <inheritdoc />
    public void Dispose() => _connection.Dispose();

    internal SqliteCommand Command(string sql, SqliteTransaction? transaction)
    {
        SqliteCommand command = _connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        return command;
    }

    internal long ReadMeta(string key, SqliteTransaction? transaction)
    {
        using SqliteCommand command = Command("SELECT value FROM meta WHERE key = $key;", transaction);
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() is long value ? value : 0;
    }

    internal bool ReadItem(ulong id, SqliteTransaction? transaction, out ulong owner, out ItemRecord item)
    {
        using SqliteCommand command = Command("SELECT owner, payload FROM items WHERE id = $id;", transaction);
        command.Parameters.AddWithValue("$id", (long)id);
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            owner = 0;
            item = null!;
            return false;
        }

        owner = (ulong)reader.GetInt64(0);
        item = _items.Decode((byte[])reader.GetValue(1));
        return true;
    }

    internal bool ReadIntent(IntentId id, SqliteTransaction? transaction, out IntentResult result)
    {
        using SqliteCommand command = Command("SELECT status, reason, detail FROM intent_dedupe WHERE session = $session AND sequence = $sequence;", transaction);
        command.Parameters.AddWithValue("$session", (long)id.Session);
        command.Parameters.AddWithValue("$sequence", (long)id.Sequence);
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            result = default;
            return false;
        }

        result = new IntentResult((IntentStatus)reader.GetByte(0), (IntentRejectReason)reader.GetByte(1), reader.GetByte(2));
        return true;
    }

    internal byte[] EncodeItem(ItemRecord item) => _items.Encode(item);

    private void Execute(string sql)
    {
        using SqliteCommand command = Command(sql, null);
        command.ExecuteNonQuery();
    }

    private void Migrate()
    {
        using (SqliteCommand probe = Command("SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = 'meta';", null))
        {
            SchemaVersion = (long)probe.ExecuteScalar()! == 0 ? 0 : (int)ReadMeta(SchemaVersionKey, null);
        }

        if (SchemaVersion > Migrations.Latest)
        {
            throw new StoreVersionException(
                $"This world was saved by a newer server (schema v{SchemaVersion}; this server supports up to v{Migrations.Latest}). Update the server.");
        }

        if (SchemaVersion == Migrations.Latest)
        {
            return;
        }

        if (SchemaVersion > 0)
        {
            Backup($"pre-migration-v{SchemaVersion}");
        }

        using SqliteTransaction transaction = _connection.BeginTransaction();
        foreach (IMigration migration in Migrations.All.Where(step => step.FromVersion >= SchemaVersion))
        {
            migration.Apply(_connection, transaction);
        }

        using (SqliteCommand set = Command("INSERT OR REPLACE INTO meta (key, value) VALUES ($key, $value);", transaction))
        {
            set.Parameters.AddWithValue("$key", SchemaVersionKey);
            set.Parameters.AddWithValue("$value", Migrations.Latest);
            set.ExecuteNonQuery();
        }

        transaction.Commit();
        SchemaVersion = Migrations.Latest;
    }

    private sealed class WorldTransaction(WorldStore store, SqliteTransaction transaction) : IWorldTransaction
    {
        public void SaveWorld(WorldSnapshot world, IReadOnlyList<PlayerSave> players) => store._world.Save(transaction, world, players);

        public bool TryGetItem(ulong id, out ulong owner, out ItemRecord item) => store.ReadItem(id, transaction, out owner, out item);

        public IReadOnlyList<ulong> ItemsOwnedBy(ulong owner)
        {
            using SqliteCommand command = store.Command("SELECT id FROM items WHERE owner = $owner ORDER BY id;", transaction);
            command.Parameters.AddWithValue("$owner", (long)owner);
            using SqliteDataReader reader = command.ExecuteReader();
            var ids = new List<ulong>();
            while (reader.Read())
            {
                ids.Add((ulong)reader.GetInt64(0));
            }

            return ids;
        }

        public void PutItem(ulong id, ulong owner, ItemRecord item)
        {
            using SqliteCommand command = store.Command("INSERT OR REPLACE INTO items (id, owner, payload) VALUES ($id, $owner, $payload);", transaction);
            command.Parameters.AddWithValue("$id", (long)id);
            command.Parameters.AddWithValue("$owner", (long)owner);
            command.Parameters.AddWithValue("$payload", store.EncodeItem(item));
            command.ExecuteNonQuery();
        }

        public void DeleteItem(ulong id)
        {
            using SqliteCommand command = store.Command("DELETE FROM items WHERE id = $id;", transaction);
            command.Parameters.AddWithValue("$id", (long)id);
            command.ExecuteNonQuery();
        }

        public bool TryGetIntent(IntentId id, out IntentResult result) => store.ReadIntent(id, transaction, out result);

        public void RecordIntent(IntentId id, IntentResult result, ulong tick)
        {
            using (SqliteCommand insert = store.Command(
                "INSERT INTO intent_dedupe (session, sequence, status, reason, detail, tick) VALUES ($session, $sequence, $status, $reason, $detail, $tick);",
                transaction))
            {
                insert.Parameters.AddWithValue("$session", (long)id.Session);
                insert.Parameters.AddWithValue("$sequence", (long)id.Sequence);
                insert.Parameters.AddWithValue("$status", (byte)result.Status);
                insert.Parameters.AddWithValue("$reason", (byte)result.Reason);
                insert.Parameters.AddWithValue("$detail", result.Detail);
                insert.Parameters.AddWithValue("$tick", (long)tick);
                insert.ExecuteNonQuery();
            }

            using SqliteCommand prune = store.Command("DELETE FROM intent_dedupe WHERE session = $session AND sequence <= $oldest;", transaction);
            prune.Parameters.AddWithValue("$session", (long)id.Session);
            prune.Parameters.AddWithValue("$oldest", (long)id.Sequence - IntentDeduplicator.Window);
            prune.ExecuteNonQuery();
        }

        public long GetMeta(string key) => store.ReadMeta(key, transaction);

        public void SetMeta(string key, long value)
        {
            using SqliteCommand command = store.Command("INSERT OR REPLACE INTO meta (key, value) VALUES ($key, $value);", transaction);
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", value);
            command.ExecuteNonQuery();
        }

        public void Commit() => transaction.Commit();

        public void Dispose() => transaction.Dispose();
    }
}
