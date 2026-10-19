using SumonerWorld.Core.Simulation;

namespace SumonerWorld.Core.Jobs;

/// <summary>
/// Work done off the simulation thread (TDD §2.5, R17). <see cref="Execute"/> runs on a worker and may only touch data the job
/// owns; <see cref="Apply"/> runs on the simulation thread and writes the result into the world. Jobs are reusable objects, so
/// submitting one does not allocate (R21).
/// </summary>
public interface IJob
{
    /// <summary>Computes the result (worker thread; job-owned data only).</summary>
    void Execute();

    /// <summary>Writes the result into the simulation (simulation thread).</summary>
    void Apply();
}

/// <summary>
/// Runs jobs off-tick and applies their results at the start of the next tick, in submission order (TDD §2.5). Because results are
/// applied at a fixed tick in a fixed order, the outcome is the same with any number of workers, including none (R11).
/// </summary>
public interface IJobScheduler
{
    /// <summary>Jobs submitted but not yet applied.</summary>
    int Pending { get; }

    /// <summary>Queues a job; returns its id (ids increase by one per job).</summary>
    long Submit(IJob job);

    /// <summary>Waits for every pending job and applies them in id order. Called once per tick (stage ApplyJobResults).</summary>
    void ApplyCompleted();
}

/// <summary>
/// <see cref="IJobScheduler"/> with a fixed number of worker threads (0 = run jobs inline when they are applied). The queue has a
/// fixed capacity; exceeding it is an error, not a silent drop (R36).
/// </summary>
public sealed class JobScheduler : IJobScheduler, IDisposable
{
    private readonly IJob?[] _jobs;
    private readonly ManualResetEventSlim[] _done;
    private readonly Thread[] _workers;
    private readonly SemaphoreSlim _work = new(0);
    private readonly object _gate = new();
    private long _nextId;
    private long _nextToApply;
    private long _nextToRun;
    private volatile bool _stopping;

    /// <summary>Creates the scheduler with <paramref name="workerCount"/> workers and room for <paramref name="capacity"/> pending jobs.</summary>
    public JobScheduler(int workerCount, int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(workerCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        _jobs = new IJob?[capacity];
        _done = new ManualResetEventSlim[capacity];
        for (int i = 0; i < capacity; i++)
        {
            _done[i] = new ManualResetEventSlim(false);
        }

        _workers = new Thread[workerCount];
        for (int i = 0; i < workerCount; i++)
        {
            _workers[i] = new Thread(WorkerLoop) { IsBackground = true, Name = $"sim-job-{i}" };
            _workers[i].Start();
        }
    }

    /// <summary>Worker threads (0 = inline).</summary>
    public int WorkerCount => _workers.Length;

    /// <inheritdoc />
    public int Pending => (int)(_nextId - _nextToApply);

    /// <inheritdoc />
    public long Submit(IJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (Pending >= _jobs.Length)
        {
            throw new InvalidOperationException($"Job queue full ({_jobs.Length} pending). Apply jobs every tick or raise the capacity.");
        }

        long id = _nextId;
        int slot = (int)(id % _jobs.Length);
        _done[slot].Reset();
        _jobs[slot] = job;
        lock (_gate)
        {
            _nextId = id + 1;
        }

        if (_workers.Length > 0)
        {
            _work.Release();
        }

        return id;
    }

    /// <inheritdoc />
    public void ApplyCompleted()
    {
        while (_nextToApply < _nextId)
        {
            int slot = (int)(_nextToApply % _jobs.Length);
            IJob job = _jobs[slot]!;
            if (_workers.Length == 0)
            {
                job.Execute();
            }
            else
            {
                _done[slot].Wait();
            }

            job.Apply();
            _jobs[slot] = null;
            _nextToApply++;
        }
    }

    /// <summary>Stops the workers.</summary>
    public void Dispose()
    {
        _stopping = true;
        if (_workers.Length > 0)
        {
            _work.Release(_workers.Length);
        }

        foreach (Thread worker in _workers)
        {
            worker.Join();
        }

        _work.Dispose();
        foreach (ManualResetEventSlim done in _done)
        {
            done.Dispose();
        }
    }

    private void WorkerLoop()
    {
        while (true)
        {
            _work.Wait();
            if (_stopping)
            {
                return;
            }

            long id;
            lock (_gate)
            {
                id = _nextToRun++;
            }

            int slot = (int)(id % _jobs.Length);
            _jobs[slot]!.Execute();
            _done[slot].Set();
        }
    }
}

/// <summary>Applies finished jobs at tick stage <see cref="SimulationStage.ApplyJobResults"/> (TDD §2.2 step 2).</summary>
public sealed class JobApplySystem(IJobScheduler jobs) : ISimulationSystem
{
    /// <inheritdoc />
    public SimulationStage Stage => SimulationStage.ApplyJobResults;

    /// <inheritdoc />
    public void Execute(in SimulationTick tick) => jobs.ApplyCompleted();
}
