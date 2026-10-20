using System.Globalization;
using Microsoft.Data.Sqlite;

namespace SumonerWorld.Persistence.Backups;

/// <summary>World backups (TDD §4.2): a consistent copy of the live database, keeping the newest few.</summary>
public interface IBackupService
{
    /// <summary>Copies <paramref name="source"/> to a new backup file; returns its path. <paramref name="reason"/> becomes part of the name.</summary>
    string Create(SqliteConnection source, string reason);

    /// <summary>Existing backups, newest first.</summary>
    IReadOnlyList<string> List();
}

/// <summary>
/// Backups with SQLite's online backup API (consistent while the server runs) into one directory, named
/// <c>world-yyyyMMdd-HHmmss-fff-reason.db</c>; only the newest <see cref="Keep"/> are kept. Timed (30 min) and shutdown backups are
/// started by the server host; the store makes one before every migration.
/// </summary>
public sealed class WorldBackups : IBackupService
{
    /// <summary>Default number of backups kept.</summary>
    public const int DefaultKeep = 10;

    private const string Prefix = "world-";
    private readonly string _directory;
    private readonly Func<DateTime> _utcNow;

    /// <summary>Creates the service; <paramref name="utcNow"/> is replaceable for tests.</summary>
    public WorldBackups(string directory, int keep = DefaultKeep, Func<DateTime>? utcNow = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(keep);
        _directory = directory;
        Keep = keep;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    /// <summary>Backups kept.</summary>
    public int Keep { get; }

    /// <inheritdoc />
    public string Create(SqliteConnection source, string reason)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (!reason.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-'))
        {
            throw new ArgumentException("Backup reasons use a-z, 0-9 and '-'.", nameof(reason));
        }

        Directory.CreateDirectory(_directory);
        string stamp = _utcNow().ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        string path = Path.Combine(_directory, $"{Prefix}{stamp}-{reason}.db");
        using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
        {
            destination.Open();
            source.BackupDatabase(destination);
        }

        foreach (string old in List().Skip(Keep))
        {
            File.Delete(old);
        }

        return path;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> List() =>
        Directory.Exists(_directory)
            ? [.. Directory.EnumerateFiles(_directory, $"{Prefix}*.db").OrderDescending(StringComparer.Ordinal)]
            : [];
}
