using Microsoft.EntityFrameworkCore;
using Shouldly;
using Warp.Core.Data.Entities;
using Warp.Core.Entities;
using Warp.Core.Enums;
using Warp.Tests.Fixtures;
using Warp.Tests.TestData.Handlers;

namespace Warp.Tests.Reliability;

[GenerateDatabaseTests(FixtureKind.Integration)]
public abstract class ConcurrencyIntegrationTestsBase : IntegrationTestBase
{
    protected ConcurrencyIntegrationTestsBase(IDatabaseFixture fixture)
        : base(fixture)
    {
    }

    [TimedFact]
    public async Task GivenFiftyCounterJobs_WithFiveWorkers_ThenAllProcessedExactlyOnce()
    {
        // The test server runs with 5 workers. Enqueue 50 counter jobs.
        // Row locking (FOR UPDATE SKIP LOCKED) ensures each job is processed exactly once.
        var publisher = Server.CreatePublisher();
        var jobIds = new List<Guid>();
        for (var i = 0; i < 50; i++)
        {
            jobIds.Add(await publisher.Enqueue(new CounterRequest()));
        }

        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await Server.WaitForCompletion();

        var ctx = Server.CreateContext();

        // All 50 jobs should be completed — proving each was processed exactly once
        var completedCount = await ctx.Set<Job>()
            .CountAsync(j => jobIds.Contains(j.Id) && j.CurrentState == State.Completed, Xunit.TestContext.Current.CancellationToken);
        completedCount.ShouldBe(50);

        // No jobs should be in any non-terminal state
        var activeCount = await ctx.Set<Job>()
            .CountAsync(
                j => jobIds.Contains(j.Id)
                    && (j.CurrentState == State.Enqueued
                        || j.CurrentState == State.Processing
                        || j.CurrentState == State.Awaiting),
                Xunit.TestContext.Current.CancellationToken);
        activeCount.ShouldBe(0);

        // Each job should have exactly one "Completed" log entry (no duplicate processing)
        foreach (var jobId in jobIds)
        {
            var completedLogs = await ctx.Set<JobLog>()
                .CountAsync(l => l.JobId == jobId && l.EventType == "Completed", Xunit.TestContext.Current.CancellationToken);
            completedLogs.ShouldBe(1, $"Job {jobId} should have exactly one Completed log");
        }
    }

    [TimedFact]
    public async Task GivenSingleJob_WithFiveWorkers_ThenOnlyOneProcessesIt()
    {
        var publisher = Server.CreatePublisher();
        var jobId = await publisher.Enqueue(new UnitRequest());
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await Server.WaitForCompletion();

        var ctx = Server.CreateContext();

        var job = await ctx.Set<Job>().FirstAsync(j => j.Id == jobId, Xunit.TestContext.Current.CancellationToken);
        job.CurrentState.ShouldBe(State.Completed);

        // Exactly one Processing and one Completed log — only one worker touched it
        var processingLogs = await ctx.Set<JobLog>()
            .CountAsync(l => l.JobId == jobId && l.EventType == "Processing", Xunit.TestContext.Current.CancellationToken);
        processingLogs.ShouldBe(1);

        var completedLogs = await ctx.Set<JobLog>()
            .CountAsync(l => l.JobId == jobId && l.EventType == "Completed", Xunit.TestContext.Current.CancellationToken);
        completedLogs.ShouldBe(1);
    }

    [TimedFact]
    public async Task GivenFiveMessages_WithFiveWorkers_ThenAllRoutedExactlyOnce()
    {
        var publisher = Server.CreatePublisher();
        var messageIds = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            messageIds.Add(await publisher.Publish(new SingleHandlerMessage()));
        }

        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await Server.WaitForCompletion();

        var ctx = Server.CreateContext();

        // Each message should be completed
        foreach (var messageId in messageIds)
        {
            var message = await ctx.Set<Job>().FirstAsync(j => j.Id == messageId, Xunit.TestContext.Current.CancellationToken);
            message.CurrentState.ShouldBe(State.Completed);

            // Each message should have exactly 1 handler job (SingleHandlerMessage has 1 handler)
            var handlerJobs = await ctx.Set<Job>()
                .Where(j => j.ParentJobId == messageId && j.Kind == JobKind.Job)
                .ToListAsync(Xunit.TestContext.Current.CancellationToken);
            handlerJobs.Count.ShouldBe(1, $"Message {messageId} should have exactly 1 handler job");
            handlerJobs[0].CurrentState.ShouldBe(State.Completed);
        }
    }
}
