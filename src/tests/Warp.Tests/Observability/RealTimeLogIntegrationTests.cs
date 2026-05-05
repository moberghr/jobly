using Microsoft.EntityFrameworkCore;
using Shouldly;
using Warp.Core.Data.Entities;
using Warp.Core.Enums;
using Warp.Tests.Fixtures;
using Warp.Tests.TestData.Handlers;

namespace Warp.Tests.Observability;

[GenerateDatabaseTests(FixtureKind.Integration)]
public abstract class RealTimeLogIntegrationTestsBase : IntegrationTestBase
{
    protected RealTimeLogIntegrationTestsBase(IDatabaseFixture fixture)
        : base(fixture)
    {
    }

    [TimedFact]
    public async Task GivenProcessingJob_WhenHandlerLogs_ThenLogsAppearInDbBeforeCompletion()
    {
        await using var server = await WarpTestServer.StartAsync(Fixture);
        var publisher = server.CreatePublisher();
        var jobId = await publisher.Enqueue(new ProgressLogRequest());
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await server.WaitForJobState(jobId, State.Processing);

        // Wait for monitor to flush logs (ticks every 1s)
        // WarpTestServer uses LogFlushInterval = 100ms, so 300ms is enough to see >= 2 flushes.
        await Task.Delay(300, Xunit.TestContext.Current.CancellationToken);

        // Verify logs are in DB while job is still processing
        var ctx = Fixture.CreateContext();
        var handlerLogs = await ctx.Set<JobLog>()
            .Where(x => x.JobId == jobId && x.EventType == "Log")
            .OrderBy(x => x.Timestamp)
            .ToListAsync(Xunit.TestContext.Current.CancellationToken);

        handlerLogs.Count.ShouldBeGreaterThanOrEqualTo(2);
        handlerLogs.ShouldContain(l => l.Message.Contains("Step 1 started"));
        handlerLogs.ShouldContain(l => l.Message.Contains("Step 1 warning"));

        // Job should still be processing (logs appeared DURING execution, not after)
        var job = await server.GetJob(jobId);
        job.CurrentState.ShouldBe(State.Processing);

        // Cancel to release the handler
        var cmd = server.CreateCommandService();
        await cmd.DeleteJob(jobId);
        await server.WaitForJobLog(jobId, "Cancelled", timeout: TimeSpan.FromSeconds(5));
    }

    [TimedFact]
    public async Task GivenProcessingJob_WhenHandlerLogs_ThenLogLevelsArePreserved()
    {
        await using var server = await WarpTestServer.StartAsync(Fixture);
        var publisher = server.CreatePublisher();
        var jobId = await publisher.Enqueue(new ProgressLogRequest());
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await server.WaitForJobState(jobId, State.Processing);

        // WarpTestServer uses LogFlushInterval = 100ms, so 300ms is enough to see >= 2 flushes.
        await Task.Delay(300, Xunit.TestContext.Current.CancellationToken);

        var ctx = Fixture.CreateContext();
        var handlerLogs = await ctx.Set<JobLog>()
            .Where(x => x.JobId == jobId && x.EventType == "Log")
            .ToListAsync(Xunit.TestContext.Current.CancellationToken);

        handlerLogs.ShouldContain(l => l.Level == "Information" && l.Message.Contains("Step 1 started"));
        handlerLogs.ShouldContain(l => l.Level == "Warning" && l.Message.Contains("Step 1 warning"));

        var cmd = server.CreateCommandService();
        await cmd.DeleteJob(jobId);
        await server.WaitForJobLog(jobId, "Cancelled", timeout: TimeSpan.FromSeconds(5));
    }

    [TimedFact]
    public async Task GivenProcessingJob_WhenCancelledBeforeFlush_ThenHandlerLogsArePreserved()
    {
        await using var server = await WarpTestServer.StartAsync(Fixture);
        var publisher = server.CreatePublisher();
        var jobId = await publisher.Enqueue(new ProgressLogRequest());
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await server.WaitForJobState(jobId, State.Processing);

        // Cancel immediately — don't wait for any flush.
        // The handler has already logged "Step 1 started" and "Step 1 warning"
        // but the monitor hasn't flushed them yet.
        // On the next monitor tick, Drain() picks up the logs AND detects cancellation.
        // The bug: monitor returns without saving the drained logs.
        var cmd = server.CreateCommandService();
        await cmd.DeleteJob(jobId);

        await server.WaitForJobLog(jobId, "Cancelled", timeout: TimeSpan.FromSeconds(5));

        // Verify handler logs survived the cancellation
        var ctx = Fixture.CreateContext();
        var handlerLogs = await ctx.Set<JobLog>()
            .Where(x => x.JobId == jobId && x.EventType == "Log")
            .ToListAsync(Xunit.TestContext.Current.CancellationToken);

        handlerLogs.Count.ShouldBeGreaterThanOrEqualTo(2);
        handlerLogs.ShouldContain(l => l.Message.Contains("Step 1 started"));
        handlerLogs.ShouldContain(l => l.Message.Contains("Step 1 warning"));
    }
}
