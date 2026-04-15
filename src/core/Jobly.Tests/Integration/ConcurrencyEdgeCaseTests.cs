using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Tests.Fixtures;
using Jobly.Tests.TestData.Handlers;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Integration;

public abstract class ConcurrencyEdgeCaseTestsBase : IntegrationTestBase
{
    protected ConcurrencyEdgeCaseTestsBase(IDatabaseFixture fixture)
        : base(fixture)
    {
    }

    [TimedFact]
    public async Task GivenConcurrentDeleteOnSameJob_ThenOnlyOneDeleteRecorded()
    {
        // Arrange — enqueue a job and wait for it to complete
        var publisher = Server.CreatePublisher();
        var jobId = await publisher.Enqueue(new UnitRequest());
        await publisher.SaveChangesAsync();

        await Server.WaitForJobState(jobId, State.Completed);

        // Act — delete from 2 threads concurrently
        // One will succeed, the other will throw (row-lock SKIP LOCKED returns null for locked row)
        var exceptions = new List<Exception>();
        var tasks = new List<Task>();
        for (var i = 0; i < 2; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var svc = Server.CreateCommandService();
                    await svc.DeleteJob(jobId);
                }
                catch (Exception ex)
                {
                    lock (exceptions)
                    {
                        exceptions.Add(ex);
                    }
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Assert — job should be Deleted, with exactly 1 Deleted log entry
        var ctx = Server.CreateContext();
        var job = await ctx.Set<Job>().FirstAsync(j => j.Id == jobId);
        job.CurrentState.ShouldBe(State.Deleted);

        var deletedLogs = await ctx.Set<JobLog>()
            .CountAsync(l => l.JobId == jobId && l.EventType == "Deleted");
        deletedLogs.ShouldBe(1);

        // The second call either got "not found" (row skipped) or saw already-deleted state
        // Either way, there should be exactly 1 successful delete
    }

    [TimedFact]
    public async Task GivenConcurrentRequeueOnSameJob_ThenStateConsistent()
    {
        // Arrange — enqueue a job and wait for it to complete
        var publisher = Server.CreatePublisher();
        var jobId = await publisher.Enqueue(new UnitRequest());
        await publisher.SaveChangesAsync();

        await Server.WaitForJobState(jobId, State.Completed);

        // Act — requeue from 2 threads concurrently
        var successCount = 0;
        var tasks = new List<Task>();
        for (var i = 0; i < 2; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var svc = Server.CreateCommandService();
                    await svc.RequeueJob(jobId);
                    Interlocked.Increment(ref successCount);
                }
                catch (ArgumentException)
                {
                    // Expected — SKIP LOCKED causes second call to not find the row
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Assert — state should be consistent (Enqueued), exactly 1 successful requeue
        var ctx = Server.CreateContext();
        var job = await ctx.Set<Job>().FirstAsync(j => j.Id == jobId);
        job.CurrentState.ShouldBe(State.Enqueued);

        var requeuedLogs = await ctx.Set<JobLog>()
            .CountAsync(l => l.JobId == jobId && l.EventType == "Requeued");
        requeuedLogs.ShouldBe(1);

        successCount.ShouldBe(1);
    }
}

[Collection<PostgreSqlIntegrationCollection>]
public class ConcurrencyEdgeCaseTests_PostgreSql : ConcurrencyEdgeCaseTestsBase
{
    public ConcurrencyEdgeCaseTests_PostgreSql(PostgreSqlIntegrationFixture fixture)
        : base(fixture)
    {
    }
}

[Collection<SqlServerIntegrationCollection>]
[Trait("Category", "SqlServer")]
public class ConcurrencyEdgeCaseTests_SqlServer : ConcurrencyEdgeCaseTestsBase
{
    public ConcurrencyEdgeCaseTests_SqlServer(SqlServerIntegrationFixture fixture)
        : base(fixture)
    {
    }
}
