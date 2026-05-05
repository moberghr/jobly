using Microsoft.EntityFrameworkCore;
using Shouldly;
using Warp.Core.Data.Entities;
using Warp.Core.Entities;
using Warp.Core.Enums;
using Warp.Tests.Fixtures;
using Warp.Tests.TestData.Handlers;

namespace Warp.Tests.Reliability;

[GenerateDatabaseTests]
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
        await using var server = await WarpTestServer.StartAsync(Fixture);
        var publisher = server.CreatePublisher();
        var jobId = await publisher.Enqueue(new UnitRequest());
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await server.WaitForJobState(jobId, State.Completed);

        // Act — delete from 2 threads concurrently
        // One will succeed, the other will throw (row-lock SKIP LOCKED returns null for locked row)
        var exceptions = new List<Exception>();
        var tasks = new List<Task>();
        for (var i = 0; i < 2; i++)
        {
            tasks.Add(Task.Run(
                async () =>
                {
                    try
                    {
                        var svc = server.CreateCommandService();
                        await svc.DeleteJob(jobId);
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions)
                        {
                            exceptions.Add(ex);
                        }
                    }
                },
                Xunit.TestContext.Current.CancellationToken));
        }

        await Task.WhenAll(tasks);

        // Assert — job should be Deleted, with exactly 1 Deleted log entry
        var ctx = Fixture.CreateContext();
        var job = await ctx.Set<Job>().FirstAsync(j => j.Id == jobId, Xunit.TestContext.Current.CancellationToken);
        job.CurrentState.ShouldBe(State.Deleted);

        var deletedLogs = await ctx.Set<JobLog>()
            .CountAsync(l => l.JobId == jobId && l.EventType == "Deleted", Xunit.TestContext.Current.CancellationToken);
        deletedLogs.ShouldBe(1);

        // The second call either got "not found" (row skipped) or saw already-deleted state
        // Either way, there should be exactly 1 successful delete
    }

    [TimedFact]
    public async Task GivenConcurrentRequeueOnSameJob_ThenStateConsistent()
    {
        // Arrange — enqueue a job and wait for it to complete
        await using var server = await WarpTestServer.StartAsync(Fixture);
        var publisher = server.CreatePublisher();
        var jobId = await publisher.Enqueue(new UnitRequest());
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await server.WaitForJobState(jobId, State.Completed);

        // Pause the server before the concurrent requeue. Without this, the fixture's worker
        // re-claims the just-requeued job at its next poll tick (~100ms) and flips the row to
        // Processing before we can assert State.Enqueued — a race with the workers, not the
        // property we're testing. Pause is how PauseIntegrationTests isolates similar races.
        var groupId = await GetFirstGroupId(server);
        var serverSvc = server.CreateServerCommandService();
        await serverSvc.PauseServer(server.ServerId);
        await server.WaitForPauseState(groupId, expectedPaused: true);

        try
        {
            // Act — requeue from 2 threads concurrently. Both should succeed (the row-level
            // WAIT lock serializes them); the second observes State.Enqueued and early-returns
            // without re-running the requeue logic.
            var tasks = new List<Task>();
            for (var i = 0; i < 2; i++)
            {
                tasks.Add(Task.Run(
                    async () =>
                    {
                        var svc = server.CreateCommandService();
                        await svc.RequeueJob(jobId);
                    },
                    Xunit.TestContext.Current.CancellationToken));
            }

            await Task.WhenAll(tasks);

            // Assert — state is consistent (Enqueued) and exactly one Requeued log was written,
            // proving the second call early-returned without performing a second requeue.
            var ctx = Fixture.CreateContext();
            var job = await ctx.Set<Job>().FirstAsync(j => j.Id == jobId, Xunit.TestContext.Current.CancellationToken);
            job.CurrentState.ShouldBe(State.Enqueued);

            var requeuedLogs = await ctx.Set<JobLog>()
                .CountAsync(l => l.JobId == jobId && l.EventType == "Requeued", Xunit.TestContext.Current.CancellationToken);
            requeuedLogs.ShouldBe(1);
        }
        finally
        {
            await serverSvc.ResumeServer(server.ServerId);
            await server.WaitForPauseState(groupId, expectedPaused: false);
        }
    }

    private async Task<Guid> GetFirstGroupId(WarpTestServer server)
    {
        var ctx = Fixture.CreateContext();
        var group = await ctx.Set<WorkerGroup>()
            .Where(g => g.ServerId == server.ServerId)
            .FirstAsync(Xunit.TestContext.Current.CancellationToken);
        return group.Id;
    }
}
