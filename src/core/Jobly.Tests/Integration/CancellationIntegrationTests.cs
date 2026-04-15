using Jobly.Core.Data.Entities;
using Jobly.Core.Enums;
using Jobly.Tests.Fixtures;
using Jobly.Tests.TestData.Handlers;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Integration;

public abstract class CancellationIntegrationTestsBase : IntegrationTestBase
{
    protected CancellationIntegrationTestsBase(IDatabaseFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task GivenProcessingJob_WhenDeleted_ThenHandlerIsCancelledAndLoggedAsCancelled()
    {
        var publisher = Server.CreatePublisher();
        var jobId = await publisher.Enqueue(new CancellableRequest());
        await publisher.SaveChangesAsync();

        // Wait for worker to pick it up
        await Server.WaitForJobState(jobId, State.Processing);

        // Cancel it (delete while processing)
        var cmd = Server.CreateCommandService();
        await cmd.DeleteJob(jobId);

        // Worker should detect state change, cancel handler, and log "Cancelled"
        // Wait for the cancellation log (not just the state change, which happens immediately from DeleteJob)
        await Server.WaitForJobLog(jobId, "Cancelled", timeout: TimeSpan.FromSeconds(15));

        var job = await Server.GetJob(jobId);
        job.CurrentState.ShouldBe(State.Deleted);
    }

    [Fact]
    public async Task GivenProcessingJob_WhenDeleted_ThenCancellationModeIsSetToGraceful()
    {
        var publisher = Server.CreatePublisher();
        var jobId = await publisher.Enqueue(new CancellableRequest());
        await publisher.SaveChangesAsync();

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

    [Fact]
    public async Task GivenProcessingJob_WhenDeleted_ThenWorkerLogsHaveWorkerId()
    {
        var publisher = Server.CreatePublisher();
        var jobId = await publisher.Enqueue(new CancellableRequest());
        await publisher.SaveChangesAsync();

        await Server.WaitForJobState(jobId, State.Processing);

        var cmd = Server.CreateCommandService();
        await cmd.DeleteJob(jobId);

        await Server.WaitForJobLog(jobId, "Cancelled", timeout: TimeSpan.FromSeconds(15));

        var logs = await Server.GetJobLogs(jobId);

        // Worker-produced logs (Processing, Cancelled) should have a WorkerId
        var processingLog = logs.FirstOrDefault(l => string.Equals(l.EventType, "Processing", StringComparison.Ordinal));
        processingLog.ShouldNotBeNull();
        processingLog.WorkerId.ShouldNotBeNull();

        var cancelledLog = logs.First(l => string.Equals(l.EventType, "Cancelled", StringComparison.Ordinal));
        cancelledLog.WorkerId.ShouldNotBeNull();
    }

    [Fact]
    public async Task GivenProcessingJob_WhenDeleted_ThenCompletesQuicklyNotAfterFullDuration()
    {
        var publisher = Server.CreatePublisher();
        var jobId = await publisher.Enqueue(new CancellableRequest());
        await publisher.SaveChangesAsync();

        await Server.WaitForJobState(jobId, State.Processing);

        var cmd = Server.CreateCommandService();
        await cmd.DeleteJob(jobId);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Server.WaitForJobState(jobId, State.Deleted, timeout: TimeSpan.FromSeconds(15));
        sw.Stop();

        // Should complete within a few seconds (CancellationCheckInterval=1s)
        // NOT the full 30s of CancellableRequest
        sw.Elapsed.TotalSeconds.ShouldBeLessThan(10);
    }
}

[Collection<PostgreSqlIntegrationCollection>]
public class CancellationIntegrationTests_PostgreSql : CancellationIntegrationTestsBase
{
    public CancellationIntegrationTests_PostgreSql(PostgreSqlIntegrationFixture fixture)
        : base(fixture)
    {
    }
}

[Collection<SqlServerIntegrationCollection>]
[Trait("Category", "SqlServer")]
public class CancellationIntegrationTests_SqlServer : CancellationIntegrationTestsBase
{
    public CancellationIntegrationTests_SqlServer(SqlServerIntegrationFixture fixture)
        : base(fixture)
    {
    }
}
