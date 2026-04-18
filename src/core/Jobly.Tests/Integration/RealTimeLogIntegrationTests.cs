using Jobly.Core.Data.Entities;
using Jobly.Core.Enums;
using Jobly.Tests.Fixtures;
using Jobly.Tests.TestData.Handlers;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Integration;

public abstract class RealTimeLogIntegrationTestsBase : IntegrationTestBase
{
    protected RealTimeLogIntegrationTestsBase(IDatabaseFixture fixture)
        : base(fixture)
    {
    }

    [TimedFact]
    public async Task GivenProcessingJob_WhenHandlerLogs_ThenLogsAppearInDbBeforeCompletion()
    {
        var publisher = Server.CreatePublisher();
        var jobId = await publisher.Enqueue(new ProgressLogRequest());
        await publisher.SaveChangesAsync();

        await Server.WaitForJobState(jobId, State.Processing);

        // Wait for monitor to flush logs (ticks every 1s)
        // JoblyTestServer uses LogFlushInterval = 100ms, so 300ms is enough to see >= 2 flushes.
        await Task.Delay(300);

        // Verify logs are in DB while job is still processing
        var ctx = Server.CreateContext();
        var handlerLogs = await ctx.Set<JobLog>()
            .Where(x => x.JobId == jobId && x.EventType == "Log")
            .OrderBy(x => x.Timestamp)
            .ToListAsync();

        handlerLogs.Count.ShouldBeGreaterThanOrEqualTo(2);
        handlerLogs.ShouldContain(l => l.Message.Contains("Step 1 started"));
        handlerLogs.ShouldContain(l => l.Message.Contains("Step 1 warning"));

        // Job should still be processing (logs appeared DURING execution, not after)
        var job = await Server.GetJob(jobId);
        job.CurrentState.ShouldBe(State.Processing);

        // Cancel to release the handler
        var cmd = Server.CreateCommandService();
        await cmd.DeleteJob(jobId);
        await Server.WaitForJobLog(jobId, "Cancelled", timeout: TimeSpan.FromSeconds(15));
    }

    [TimedFact]
    public async Task GivenProcessingJob_WhenHandlerLogs_ThenLogLevelsArePreserved()
    {
        var publisher = Server.CreatePublisher();
        var jobId = await publisher.Enqueue(new ProgressLogRequest());
        await publisher.SaveChangesAsync();

        await Server.WaitForJobState(jobId, State.Processing);

        // JoblyTestServer uses LogFlushInterval = 100ms, so 300ms is enough to see >= 2 flushes.
        await Task.Delay(300);

        var ctx = Server.CreateContext();
        var handlerLogs = await ctx.Set<JobLog>()
            .Where(x => x.JobId == jobId && x.EventType == "Log")
            .ToListAsync();

        handlerLogs.ShouldContain(l => l.Level == "Information" && l.Message.Contains("Step 1 started"));
        handlerLogs.ShouldContain(l => l.Level == "Warning" && l.Message.Contains("Step 1 warning"));

        var cmd = Server.CreateCommandService();
        await cmd.DeleteJob(jobId);
        await Server.WaitForJobLog(jobId, "Cancelled", timeout: TimeSpan.FromSeconds(15));
    }

    [TimedFact]
    public async Task GivenProcessingJob_WhenCancelledBeforeFlush_ThenHandlerLogsArePreserved()
    {
        var publisher = Server.CreatePublisher();
        var jobId = await publisher.Enqueue(new ProgressLogRequest());
        await publisher.SaveChangesAsync();

        await Server.WaitForJobState(jobId, State.Processing);

        // Cancel immediately — don't wait for any flush.
        // The handler has already logged "Step 1 started" and "Step 1 warning"
        // but the monitor hasn't flushed them yet.
        // On the next monitor tick, Drain() picks up the logs AND detects cancellation.
        // The bug: monitor returns without saving the drained logs.
        var cmd = Server.CreateCommandService();
        await cmd.DeleteJob(jobId);

        await Server.WaitForJobLog(jobId, "Cancelled", timeout: TimeSpan.FromSeconds(15));

        // Verify handler logs survived the cancellation
        var ctx = Server.CreateContext();
        var handlerLogs = await ctx.Set<JobLog>()
            .Where(x => x.JobId == jobId && x.EventType == "Log")
            .ToListAsync();

        handlerLogs.Count.ShouldBeGreaterThanOrEqualTo(2);
        handlerLogs.ShouldContain(l => l.Message.Contains("Step 1 started"));
        handlerLogs.ShouldContain(l => l.Message.Contains("Step 1 warning"));
    }
}

[Collection<PostgreSqlIntegrationCollection>]
public class RealTimeLogIntegrationTests_PostgreSql : RealTimeLogIntegrationTestsBase
{
    public RealTimeLogIntegrationTests_PostgreSql(PostgreSqlIntegrationFixture fixture)
        : base(fixture)
    {
    }
}

[Collection<SqlServerIntegrationCollection>]
[Trait("Category", "SqlServer")]
public class RealTimeLogIntegrationTests_SqlServer : RealTimeLogIntegrationTestsBase
{
    public RealTimeLogIntegrationTests_SqlServer(SqlServerIntegrationFixture fixture)
        : base(fixture)
    {
    }
}
