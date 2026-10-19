using SumonerWorld.Core.Jobs;

namespace SumonerWorld.Tests.Core.Jobs;

public class JobSchedulerTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public void Results_are_applied_in_submission_order_whatever_the_number_of_workers(int workers)
    {
        using var scheduler = new JobScheduler(workers, capacity: 64);
        var applied = new List<int>();
        var jobs = Enumerable.Range(0, 50).Select(i => new SquareJob(i, applied)).ToArray();

        foreach (SquareJob job in jobs)
        {
            scheduler.Submit(job);
        }

        Assert.Equal(50, scheduler.Pending);
        scheduler.ApplyCompleted();

        Assert.Equal(Enumerable.Range(0, 50).Select(i => i * i), applied);
        Assert.Equal(0, scheduler.Pending);
    }

    [Fact]
    public void Execute_runs_off_the_simulation_thread_when_there_are_workers()
    {
        using var scheduler = new JobScheduler(workerCount: 2, capacity: 4);
        var job = new ThreadRecordingJob();

        scheduler.Submit(job);
        scheduler.ApplyCompleted();

        Assert.NotEqual(Environment.CurrentManagedThreadId, job.ExecuteThread);
        Assert.Equal(Environment.CurrentManagedThreadId, job.ApplyThread);
    }

    [Fact]
    public void Ids_increase_by_one_and_a_full_queue_fails_loudly()
    {
        using var scheduler = new JobScheduler(workerCount: 0, capacity: 2);
        var applied = new List<int>();

        Assert.Equal(0, scheduler.Submit(new SquareJob(1, applied)));
        Assert.Equal(1, scheduler.Submit(new SquareJob(2, applied)));
        Assert.Throws<InvalidOperationException>(() => scheduler.Submit(new SquareJob(3, applied)));

        scheduler.ApplyCompleted();
        Assert.Equal(2, scheduler.Submit(new SquareJob(3, applied)));
    }

    [Fact]
    public void Submitting_and_applying_reused_jobs_does_not_allocate()
    {
        using var scheduler = new JobScheduler(workerCount: 0, capacity: 8);
        var job = new CountingJob();
        scheduler.Submit(job);
        scheduler.ApplyCompleted();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            scheduler.Submit(job);
            scheduler.ApplyCompleted();
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(10_001, job.Applied);
    }

    private sealed class SquareJob(int value, List<int> applied) : IJob
    {
        private int _result;

        public void Execute() => _result = value * value;

        public void Apply() => applied.Add(_result);
    }

    private sealed class ThreadRecordingJob : IJob
    {
        public int ExecuteThread { get; private set; }

        public int ApplyThread { get; private set; }

        public void Execute() => ExecuteThread = Environment.CurrentManagedThreadId;

        public void Apply() => ApplyThread = Environment.CurrentManagedThreadId;
    }

    private sealed class CountingJob : IJob
    {
        public int Applied { get; private set; }

        public void Execute()
        {
        }

        public void Apply() => Applied++;
    }
}
