using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Tests.TestData.Handlers;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Jobs;

public abstract partial class JoblyTests : TestBase
{
    [Fact]
    public async Task GivenProcessingJob_WhenDeleted_ThenHandlerIsCancelledAndJobLogsEvent()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var jobId = await publisher.Enqueue(new CancellableRequest());
        await context.SaveChangesAsync();

        await EnsureServerRegistered();

        // Start worker in background — it will pick up the slow job
        var worker = TestUtils.CreateJoblyWorkerService(_serviceScopeFactory);
        var workerTask = Task.Run(async () => await worker.GetAndProcessJob(CancellationToken.None));

        // Wait for job to be picked up (state = Processing)
        await WaitForJobState(jobId, State.Processing, TimeSpan.FromSeconds(5));

        // Delete the job while it's processing — this triggers cancellation
        var commandService = TestUtils.CreateJobCommandService(CreateContext());
        await commandService.DeleteJob(jobId);

        // Worker should detect the state change and cancel the handler
        await workerTask;

        // Verify: job is Deleted with a "Cancelled" log entry
        var job = await GetJob(jobId);
        job.CurrentState.ShouldBe(State.Deleted);
        job.CurrentWorkerId.ShouldBeNull();

        var logs = await CreateContext().Set<JobLog>()
            .Where(x => x.JobId == jobId)
            .OrderBy(x => x.Timestamp)
            .ToListAsync();

        logs.ShouldContain(l => l.EventType == "Cancelled");
    }

    [Fact]
    public async Task GivenProcessingJob_WhenDeleted_ThenJobCompletesWithinCancellationInterval()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var jobId = await publisher.Enqueue(new CancellableRequest());
        await context.SaveChangesAsync();

        await EnsureServerRegistered();

        var worker = TestUtils.CreateJoblyWorkerService(_serviceScopeFactory);
        var workerTask = Task.Run(async () => await worker.GetAndProcessJob(CancellationToken.None));

        await WaitForJobState(jobId, State.Processing, TimeSpan.FromSeconds(5));

        // Delete and time how long until worker finishes
        var commandService = TestUtils.CreateJobCommandService(CreateContext());
        await commandService.DeleteJob(jobId);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await workerTask;
        sw.Stop();

        // Should complete within ~10 seconds (CancellationCheckInterval default 5s + margin)
        // NOT the full 30 seconds of the slow handler
        sw.Elapsed.TotalSeconds.ShouldBeLessThan(15);
    }

    private async Task WaitForJobState(Guid jobId, State expectedState, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var state = await CreateContext().Set<Job>()
                .Where(x => x.Id == jobId)
                .Select(x => x.CurrentState)
                .FirstOrDefaultAsync();

            if (state == expectedState)
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Job {jobId} did not reach state {expectedState} within {timeout}");
    }
}
