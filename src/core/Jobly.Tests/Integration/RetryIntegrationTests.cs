using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Tests.Fixtures;
using Jobly.Tests.TestData.Handlers;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Integration;

public abstract class RetryIntegrationTestsBase : IntegrationTestBase
{
    protected RetryIntegrationTestsBase(IDatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task GivenFailingJobWithThreeRetries_WhenProcessed_ThenRetriesThreeTimesThenFails()
    {
        var publisher = _server.CreatePublisher();
        var jobId = await publisher.Enqueue(new ThrowExceptionRequest(), maxRetries: 3);
        await publisher.SaveChangesAsync();

        await _server.WaitForJobState(jobId, State.Failed, timeout: TimeSpan.FromSeconds(30));

        var ctx = _server.CreateContext();

        var job = await ctx.Set<Job>().FirstAsync(j => j.Id == jobId);
        job.CurrentState.ShouldBe(State.Failed);
        job.MaxRetries.ShouldBe(3);
        job.RetriedTimes.ShouldBe(3);

        // Should have 1 initial attempt + 3 retries = 4 "Processing" log entries
        var processingLogs = await ctx.Set<JobLog>()
            .CountAsync(l => l.JobId == jobId && l.EventType == "Processing");
        processingLogs.ShouldBe(4);

        // Should have 3 "Requeued" log entries (one per retry)
        var requeuedLogs = await ctx.Set<JobLog>()
            .CountAsync(l => l.JobId == jobId && l.EventType == "Requeued");
        requeuedLogs.ShouldBe(3);

        // Should have 1 "Failed" log entry (final failure)
        var failedLogs = await ctx.Set<JobLog>()
            .CountAsync(l => l.JobId == jobId && l.EventType == "Failed");
        failedLogs.ShouldBe(1);
    }

    [Fact]
    public async Task GivenFailingJobWithZeroRetries_WhenProcessed_ThenFailsImmediately()
    {
        var publisher = _server.CreatePublisher();
        var jobId = await publisher.Enqueue(new ThrowExceptionRequest(), maxRetries: 0);
        await publisher.SaveChangesAsync();

        await _server.WaitForJobState(jobId, State.Failed, timeout: TimeSpan.FromSeconds(15));

        var ctx = _server.CreateContext();

        var job = await ctx.Set<Job>().FirstAsync(j => j.Id == jobId);
        job.CurrentState.ShouldBe(State.Failed);
        job.MaxRetries.ShouldBe(0);
        job.RetriedTimes.ShouldBe(0);

        // Should have exactly 1 "Processing" log entry (the single attempt)
        var processingLogs = await ctx.Set<JobLog>()
            .CountAsync(l => l.JobId == jobId && l.EventType == "Processing");
        processingLogs.ShouldBe(1);

        // Should have 0 "Requeued" entries (no retries)
        var requeuedLogs = await ctx.Set<JobLog>()
            .CountAsync(l => l.JobId == jobId && l.EventType == "Requeued");
        requeuedLogs.ShouldBe(0);

        // Should have 1 "Failed" log entry
        var failedLogs = await ctx.Set<JobLog>()
            .CountAsync(l => l.JobId == jobId && l.EventType == "Failed");
        failedLogs.ShouldBe(1);
    }
}

[Collection("PostgreSql")]
public class RetryIntegrationTests_PostgreSql : RetryIntegrationTestsBase
{
    public RetryIntegrationTests_PostgreSql(PostgreSqlFixture fixture) : base(fixture) { }
}

[Collection("SqlServer")]
[Trait("Category", "SqlServer")]
public class RetryIntegrationTests_SqlServer : RetryIntegrationTestsBase
{
    public RetryIntegrationTests_SqlServer(SqlServerFixture fixture) : base(fixture) { }
}
