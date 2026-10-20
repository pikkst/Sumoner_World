using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Core.World;

namespace SumonerWorld.Persistence;

/// <summary>
/// The persistence thread (TDD §4.2): the simulation hands over a snapshot every 30 s and carries on; this thread writes it in one
/// transaction. Only the newest snapshot waiting is written (an older one still queued is replaced). After a write it makes a timed
/// backup when <see cref="BackupEveryTicks"/> have passed. The store must not be used by other threads while the writer runs.
/// </summary>
public sealed class CheckpointWriter : IDisposable
{
    /// <summary>30 s at 30 Hz.</summary>
    public const ulong DefaultIntervalTicks = 900;

    /// <summary>30 min at 30 Hz.</summary>
    public const ulong DefaultBackupEveryTicks = 54_000;

    private readonly IWorldStore _store;
    private readonly ILog _log;
    private readonly Thread _thread;
    private readonly object _gate = new();
    private (WorldSnapshot World, IReadOnlyList<PlayerSave> Players)? _pending;
    private bool _writing;
    private bool _stopping;
    private ulong _lastBackupTick;
    private long _lastSavedTick = -1;
    private int _saves;

    /// <summary>Starts the writer thread.</summary>
    public CheckpointWriter(IWorldStore store, ILog log, ulong backupEveryTicks = DefaultBackupEveryTicks)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(log);
        _store = store;
        _log = log;
        BackupEveryTicks = backupEveryTicks;
        _thread = new Thread(Run) { IsBackground = true, Name = "World checkpoint writer" };
        _thread.Start();
    }

    /// <summary>Ticks between timed backups.</summary>
    public ulong BackupEveryTicks { get; }

    /// <summary>Tick of the newest snapshot written (−1 before the first).</summary>
    public long LastSavedTick => Interlocked.Read(ref _lastSavedTick);

    /// <summary>Snapshots written since the start.</summary>
    public int Saves => Volatile.Read(ref _saves);

    /// <summary>The last write error, if any (the writer keeps going; the next snapshot retries).</summary>
    public Exception? LastError { get; private set; }

    /// <summary>Queues a snapshot; returns at once.</summary>
    public void Submit(WorldSnapshot world, IReadOnlyList<PlayerSave> players)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(players);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            _pending = (world, players);
            Monitor.PulseAll(_gate);
        }
    }

    /// <summary>Waits until everything submitted is written; false on timeout.</summary>
    public bool Flush(TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        lock (_gate)
        {
            while (_pending is not null || _writing)
            {
                TimeSpan left = deadline - DateTime.UtcNow;
                if (left <= TimeSpan.Zero || !Monitor.Wait(_gate, left))
                {
                    return _pending is null && !_writing;
                }
            }
        }

        return true;
    }

    /// <summary>Writes what is pending, then stops the thread.</summary>
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
            (WorldSnapshot World, IReadOnlyList<PlayerSave> Players) next;
            lock (_gate)
            {
                while (_pending is null && !_stopping)
                {
                    Monitor.Wait(_gate);
                }

                if (_pending is null)
                {
                    return;
                }

                next = _pending.Value;
                _pending = null;
                _writing = true;
            }

            Write(next.World, next.Players);
            lock (_gate)
            {
                _writing = false;
                Monitor.PulseAll(_gate);
            }
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
#pragma warning disable CA1031 // The writer thread must survive any write error; the next snapshot retries and the error is reported.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LastError = exception;
            _log.Write(LogLevel.Error, "world.save_failed", $"tick {world.Tick}: {exception.Message}");
        }
    }
}
