using Microsoft.EntityFrameworkCore;
using Shouldly;
using Warp.Core.Data.Entities;
using Warp.Core.Enums;
using Warp.Tests.Fixtures;
using Warp.Tests.TestData.Handlers;

namespace Warp.Tests.Features.Cancellation;

[GenerateDatabaseTests(FixtureKind.Integration)]
public abstract class CancellationIntegrationTestsBase : IntegrationTestBase
{
    protected CancellationIntegrationTestsBase(IDatabaseFixture fixture)
        : base(fixture)
    {
    }

    [TimedFact]
    public async Task GivenProcessingJob_WhenDeleted_ThenHandlerIsCancelledAndLoggedAsCancelled()
    {
        var publisher = Server.CreatePublisher();
        var jobId = await publisher.Enqueue(new CancellableRequest());
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Wait for worker to pick it up
        await Server.WaitForJobState(jobId, State.Processing);

        // Cancel it (delete while processing)
        var cmd = Server.CreateCommandService();
        await cmd.DeleteJob(jobId);

        // Worker should detect state change, cancel handler, and log "Cancelled"
        // Wait for the cancellation log (not just the state change, which happens immediately from DeleteJob)
        await Server.WaitForJobLog(jobId, "Cancelled", timeout: TimeSpan.FromSeconds(5));

        var job = await Server.GetJob(jobId);
        job.CurrentState.ShouldBe(State.Deleted);
    }

    [TimedFact]
    public async Task GivenProcessingJob_WhenDeleted_ThenCancellationModeIsSetToGraceful()
    {
        var publisher = Server.CreatePublisher();
        var jobId = await publisher.Enqueue(new CancellableRequest());
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await Server.WaitForJobState(jobId, State.Processing);

        var cmd = Server.CreateCommandService();
        await cmd.DeleteJob(jobId);

        // Immediately after DeleteJob, the job should have CancellationMode=Graceful and still be Processing
        var job = await Server.GetJob(jobId);
        job.CancellationMode.ShouldBe(CancellationMode.Graceful);

        // After the worker processes cancellation, verify the CancellationRequested log exists
        await Server.WaitForJobLog(jobId, "CancellationRequested", timeout: TimeSpan.FromSeconds(5));

        var logs = await Server.GetJobLogs(jobId);
        var cancellationLog = logs.First(l => string.Equals(l.EventType, "CancellationRequested", StringComparison.Ordinal));
        cancellationLog.WorkerId.ShouldBeNull();
    }

    [TimedFact]
    public async Task GivenProcessingJob_WhenDeleted_ThenWorkerLogsHaveWorkerId()
    {
        var publisher = Server.CreatePublisher();
        var jobId = await publisher.Enqueue(new CancellableRequest());
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await Server.WaitForJobState(jobId, State.Processing);

        var cmd = Server.CreateCommandService();
        await cmd.DeleteJob(jobId);

        await Server.WaitForJobLog(jobId, "Cancelled", timeout: TimeSpan.FromSeconds(5));

        var logs = await Server.GetJobLogs(jobId);

        // Worker-produced logs (Processing, Cancelled) should have a WorkerId
        var processingLog = logs.FirstOrDefault(l => string.Equals(l.EventType, "Processing", StringComparison.Ordinal));
        processingLog.ShouldNotBeNull();
        processingLog.WorkerId.ShouldNotBeNull();

        var cancelledLog = logs.First(l => string.Equals(l.EventType, "Cancelled", StringComparison.Ordinal));
        cancelledLog.WorkerId.ShouldNotBeNull();
    }

    [TimedFact]
    public async Task GivenProcessingJob_WhenDeleted_ThenCompletesQuicklyNotAfterFullDuration()
    {
        var publisher = Server.CreatePublisher();
        var jobId = await publisher.Enqueue(new CancellableRequest());
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await Server.WaitForJobState(jobId, State.Processing);

        var cmd = Server.CreateCommandService();
        await cmd.DeleteJob(jobId);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Server.WaitForJobState(jobId, State.Deleted, timeout: TimeSpan.FromSeconds(5));
        sw.Stop();

        // Should complete within a few seconds (CancellationCheckInterval=1s)
        // NOT the full 30s of CancellableRequest
        sw.Elapsed.TotalSeconds.ShouldBeLessThan(10);
    }
}
