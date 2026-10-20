using Microsoft.Data.Sqlite;

namespace SumonerWorld.Persistence.Schema;

/// <summary>One step of the database layout, <see cref="FromVersion"/> → <see cref="ToVersion"/> (TDD §4.3). Steps run in order inside one transaction.</summary>
public interface IMigration
{
    /// <summary>Schema version before the step.</summary>
    int FromVersion { get; }

    /// <summary>Schema version after the step.</summary>
    int ToVersion { get; }

    /// <summary>Applies the step.</summary>
    void Apply(SqliteConnection connection, SqliteTransaction transaction);
}

/// <summary>The ordered list of every migration ever shipped. Never edit a shipped step: add a new one.</summary>
public static class Migrations
{
    /// <summary>All steps, oldest first.</summary>
    public static IReadOnlyList<IMigration> All { get; } = [new CreateWorldV1(), new AddWorldTablesV2()];

    /// <summary>The newest schema version.</summary>
    public static int Latest => All[^1].ToVersion;

    /// <summary>Runs one SQL statement inside a migration.</summary>
    public static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        ArgumentNullException.ThrowIfNull(connection);
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}

/// <summary>
/// v0 → v1 (M1-17a): <c>meta</c> (key → integer), <c>items</c> (id, owner, payload) and <c>intent_dedupe</c> (TDD §3.4: written in the
/// same transaction as the effect). Players, creatures and reputation follow in v2.
/// </summary>
internal sealed class CreateWorldV1 : IMigration
{
    public int FromVersion => 0;

    public int ToVersion => 1;

    public void Apply(SqliteConnection connection, SqliteTransaction transaction)
    {
        Migrations.Execute(connection, transaction, "CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value INTEGER NOT NULL) WITHOUT ROWID;");
        Migrations.Execute(connection, transaction, "CREATE TABLE items (id INTEGER PRIMARY KEY, owner INTEGER NOT NULL, payload BLOB NOT NULL);");
        Migrations.Execute(connection, transaction, "CREATE INDEX items_by_owner ON items (owner);");
        Migrations.Execute(
            connection,
            transaction,
            "CREATE TABLE intent_dedupe (session INTEGER NOT NULL, sequence INTEGER NOT NULL, status INTEGER NOT NULL, reason INTEGER NOT NULL, " +
            "detail INTEGER NOT NULL, tick INTEGER NOT NULL, PRIMARY KEY (session, sequence)) WITHOUT ROWID;");
    }
}

/// <summary>
/// v1 → v2 (M1-17b): <c>players</c> (account, payload), <c>creatures</c> (stable id, payload with the mind) and <c>reputation</c>
/// (species id, region, player, score). The world meta values (tick, random state, last stable id) live in <c>meta</c>.
/// </summary>
internal sealed class AddWorldTablesV2 : IMigration
{
    public int FromVersion => 1;

    public int ToVersion => 2;

    public void Apply(SqliteConnection connection, SqliteTransaction transaction)
    {
        Migrations.Execute(connection, transaction, "CREATE TABLE players (account INTEGER PRIMARY KEY, payload BLOB NOT NULL);");
        Migrations.Execute(connection, transaction, "CREATE TABLE creatures (stable_id INTEGER PRIMARY KEY, payload BLOB NOT NULL);");
        Migrations.Execute(
            connection,
            transaction,
            "CREATE TABLE reputation (species TEXT NOT NULL, region INTEGER NOT NULL, player INTEGER NOT NULL, score INTEGER NOT NULL, " +
            "PRIMARY KEY (species, region, player)) WITHOUT ROWID;");
    }
}
