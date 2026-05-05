using Microsoft.EntityFrameworkCore;
using Shouldly;
using Warp.Core.Data.Entities;
using Warp.Core.Enums;
using Warp.Tests.Fixtures;
using Warp.Tests.TestData.Handlers;

namespace Warp.Tests.Features.Cancellation;

[GenerateDatabaseTests]
public abstract class CancellationIntegrationTestsBase : IntegrationTestBase
{
    protected CancellationIntegrationTestsBase(IDatabaseFixture fixture)
        : base(fixture)
    {
    }

    [TimedFact]
    public async Task GivenProcessingJob_WhenDeleted_ThenHandlerIsCancelledAndLoggedAsCancelled()
    {
        await using var server = await WarpTestServer.StartAsync(Fixture);
        var publisher = server.CreatePublisher();
        var jobId = await publisher.Enqueue(new CancellableRequest());
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Wait for worker to pick it up
        await server.WaitForJobState(jobId, State.Processing);

        // Cancel it (delete while processing)
        var cmd = server.CreateCommandService();
        await cmd.DeleteJob(jobId);

        // Worker should detect state change, cancel handler, and log "Cancelled"
        // Wait for the cancellation log (not just the state change, which happens immediately from DeleteJob)
        await server.WaitForJobLog(jobId, "Cancelled", timeout: TimeSpan.FromSeconds(5));

        var job = await server.GetJob(jobId);
        job.CurrentState.ShouldBe(State.Deleted);
    }

    [TimedFact]
    public async Task GivenProcessingJob_WhenDeleted_ThenCancellationModeIsSetToGraceful()
    {
        await using var server = await WarpTestServer.StartAsync(Fixture);
        var publisher = server.CreatePublisher();
        var jobId = await publisher.Enqueue(new CancellableRequest());
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await server.WaitForJobState(jobId, State.Processing);

        var cmd = server.CreateCommandService();
        await cmd.DeleteJob(jobId);

        // Immediately after DeleteJob, the job should have CancellationMode=Graceful and still be Processing
        var job = await server.GetJob(jobId);
        job.CancellationMode.ShouldBe(CancellationMode.Graceful);

        // After the worker processes cancellation, verify the CancellationRequested log exists
        await server.WaitForJobLog(jobId, "CancellationRequested", timeout: TimeSpan.FromSeconds(5));

        var logs = await server.GetJobLogs(jobId);
        var cancellationLog = logs.First(l => string.Equals(l.EventType, "CancellationRequested", StringComparison.Ordinal));
        cancellationLog.WorkerId.ShouldBeNull();

        // Wait for the handler to actually exit before dispose so server shutdown isn't blocked.
        await server.WaitForJobState(jobId, State.Deleted, timeout: TimeSpan.FromSeconds(5));
    }

    [TimedFact]
    public async Task GivenProcessingJob_WhenDeleted_ThenWorkerLogsHaveWorkerId()
    {
        await using var server = await WarpTestServer.StartAsync(Fixture);
        var publisher = server.CreatePublisher();
        var jobId = await publisher.Enqueue(new CancellableRequest());
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await server.WaitForJobState(jobId, State.Processing);

        var cmd = server.CreateCommandService();
        await cmd.DeleteJob(jobId);

        await server.WaitForJobLog(jobId, "Cancelled", timeout: TimeSpan.FromSeconds(5));

        var logs = await server.GetJobLogs(jobId);

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
        await using var server = await WarpTestServer.StartAsync(Fixture);
        var publisher = server.CreatePublisher();
        var jobId = await publisher.Enqueue(new CancellableRequest());
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await server.WaitForJobState(jobId, State.Processing);

        var cmd = server.CreateCommandService();
        await cmd.DeleteJob(jobId);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await server.WaitForJobState(jobId, State.Deleted, timeout: TimeSpan.FromSeconds(5));
        sw.Stop();

        // Should complete within a few seconds (CancellationCheckInterval=1s)
        // NOT the full 30s of CancellableRequest
        sw.Elapsed.TotalSeconds.ShouldBeLessThan(10);
    }
}
