using System.Collections.Concurrent;
using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Transactions;
using SumonerWorld.Core.World;

namespace SumonerWorld.Persistence;

/// <summary>
/// The persistence thread (TDD §4.2). Two kinds of work, written in the order they arrive:
/// <list type="bullet">
/// <item><b>Journal entries</b> (<see cref="ITransactionJournal"/>): a processed intent's changed creatures and its dedupe record, in
/// one transaction; the entry is then reported durable so the server may send the result.</item>
/// <item><b>Checkpoints</b> every 30 s: the whole world in one transaction. A checkpoint still waiting is replaced by a newer one.
/// After a checkpoint a timed backup is made when <see cref="BackupEveryTicks"/> have passed.</item>
/// </list>
/// The store must not be used by other threads while the writer runs.
/// </summary>
public sealed class PersistenceWriter : ITransactionJournal, IDisposable
{
    /// <summary>30 s at 30 Hz.</summary>
    public const ulong DefaultIntervalTicks = 900;

    /// <summary>30 min at 30 Hz.</summary>
    public const ulong DefaultBackupEveryTicks = 54_000;

    private readonly IWorldStore _store;
    private readonly ILog _log;
    private readonly Thread _thread;
    private readonly object _gate = new();
    private readonly LinkedList<Work> _queue = new();
    private readonly ConcurrentQueue<IntentId> _durable = new();
    private bool _writing;
    private bool _stopping;
    private ulong _lastBackupTick;
    private long _lastSavedTick = -1;
    private int _saves;
    private int _journaled;

    /// <summary>Starts the writer thread.</summary>
    public PersistenceWriter(IWorldStore store, ILog log, ulong backupEveryTicks = DefaultBackupEveryTicks)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(log);
        _store = store;
        _log = log;
        BackupEveryTicks = backupEveryTicks;
        _thread = new Thread(Run) { IsBackground = true, Name = "World persistence writer" };
        _thread.Start();
    }

    /// <summary>Ticks between timed backups.</summary>
    public ulong BackupEveryTicks { get; }

    /// <summary>Tick of the newest checkpoint written (−1 before the first).</summary>
    public long LastSavedTick => Interlocked.Read(ref _lastSavedTick);

    /// <summary>Checkpoints written since the start.</summary>
    public int Saves => Volatile.Read(ref _saves);

    /// <summary>Journal entries written since the start.</summary>
    public int Journaled => Volatile.Read(ref _journaled);

    /// <summary>The last write error, if any. A failed journal entry is never reported durable.</summary>
    public Exception? LastError { get; private set; }

    /// <summary>Queues a checkpoint; returns at once.</summary>
    public void Submit(WorldSnapshot world, IReadOnlyList<PlayerSave> players)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(players);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            for (LinkedListNode<Work>? node = _queue.First; node is not null; node = node.Next)
            {
                if (node.Value.Checkpoint is not null)
                {
                    _queue.Remove(node);
                    break;
                }
            }

            _queue.AddLast(new Work(null, (world, players)));
            Monitor.PulseAll(_gate);
        }
    }

    /// <inheritdoc />
    public void Append(JournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            _queue.AddLast(new Work(entry, null));
            Monitor.PulseAll(_gate);
        }
    }

    /// <inheritdoc />
    public bool TryTakeDurable(out IntentId id) => _durable.TryDequeue(out id);

    /// <summary>Waits until everything queued is written; false on timeout.</summary>
    public bool Flush(TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        lock (_gate)
        {
            while (_queue.Count > 0 || _writing)
            {
                TimeSpan left = deadline - DateTime.UtcNow;
                if (left <= TimeSpan.Zero || !Monitor.Wait(_gate, left))
                {
                    return _queue.Count == 0 && !_writing;
                }
            }
        }

        return true;
    }

    /// <summary>Writes what is queued, then stops the thread.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _stopping = true;
            Monitor.PulseAll(_gate);
        }

        _thread.Join();
    }

    private void Run()
    {
        while (true)
        {
            Work next;
            lock (_gate)
            {
                while (_queue.Count == 0 && !_stopping)
                {
                    Monitor.Wait(_gate);
                }

                if (_queue.Count == 0)
                {
                    return;
                }

                next = _queue.First!.Value;
                _queue.RemoveFirst();
                _writing = true;
            }

            if (next.Entry is not null)
            {
                Write(next.Entry);
            }
            else
            {
                Write(next.Checkpoint!.Value.World, next.Checkpoint.Value.Players);
            }

            lock (_gate)
            {
                _writing = false;
                Monitor.PulseAll(_gate);
            }
        }
    }

    private void Write(JournalEntry entry)
    {
        try
        {
            using (IWorldTransaction transaction = _store.Begin())
            {
                foreach (CreatureSave creature in entry.Changed)
                {
                    transaction.PutCreature(creature);
                }

                if (entry.IsNew)
                {
                    transaction.RecordIntent(entry.Id, entry.Result, entry.Tick);
                }

                transaction.Commit();
            }

            Interlocked.Increment(ref _journaled);
            _durable.Enqueue(entry.Id);
        }
#pragma warning disable CA1031 // The writer thread must survive any write error; it is reported and the entry is not acknowledged.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LastError = exception;
            _log.Write(LogLevel.Error, "journal.write_failed", $"intent {entry.Id}: {exception.Message}; its result is not sent");
        }
    }

    private void Write(WorldSnapshot world, IReadOnlyList<PlayerSave> players)
    {
        try
        {
            using (IWorldTransaction transaction = _store.Begin())
            {
                transaction.SaveWorld(world, players);
                transaction.Commit();
            }

            Interlocked.Exchange(ref _lastSavedTick, (long)world.Tick);
            Interlocked.Increment(ref _saves);
            if (world.Tick - _lastBackupTick >= BackupEveryTicks)
            {
                _store.Backup("timed");
                _lastBackupTick = world.Tick;
            }

            if (_log.IsEnabled(LogLevel.Debug))
            {
                _log.Write(LogLevel.Debug, "world.saved", $"tick {world.Tick}: {world.Creatures.Count} creatures, {players.Count} players");
            }
        }
#pragma warning disable CA1031 // The writer thread must survive any write error; the next checkpoint retries and the error is reported.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LastError = exception;
            _log.Write(LogLevel.Error, "world.save_failed", $"tick {world.Tick}: {exception.Message}");
        }
    }

    private readonly record struct Work(JournalEntry? Entry, (WorldSnapshot World, IReadOnlyList<PlayerSave> Players)? Checkpoint);
}
