using Microsoft.EntityFrameworkCore;
using Shouldly;
using Warp.Core.Data.Entities;
using Warp.Core.Enums;
using Warp.Tests.Fixtures;
using Warp.Tests.TestData.Handlers;

namespace Warp.Tests.Features.Pause;

[GenerateDatabaseTests(FixtureKind.Integration)]
public abstract class PauseIntegrationTestsBase : IntegrationTestBase
{
    protected PauseIntegrationTestsBase(IDatabaseFixture fixture)
        : base(fixture)
    {
    }

    private async Task<Guid> GetFirstGroupId()
    {
        var ctx = Server.CreateContext();
        var group = await ctx.Set<WorkerGroup>()
            .Where(g => g.ServerId == Server.ServerId)
            .FirstAsync(Xunit.TestContext.Current.CancellationToken);
        return group.Id;
    }

    [TimedFact]
    public async Task PauseServer_PauseStateHolder_UpdatedByHeartbeat()
    {
        var groupId = await GetFirstGroupId();
        var svc = Server.CreateServerCommandService();

        await svc.PauseServer(Server.ServerId);
        await Server.WaitForPauseState(groupId, expectedPaused: true);
        Server.PauseState.IsPaused(groupId).ShouldBeTrue();

        // Cleanup
        await svc.ResumeServer(Server.ServerId);
        await Server.WaitForPauseState(groupId, expectedPaused: false);
    }

    [TimedFact]
    public async Task PauseServer_JobsStayEnqueued()
    {
        var groupId = await GetFirstGroupId();
        var svc = Server.CreateServerCommandService();

        // Pause and wait for propagation
        await svc.PauseServer(Server.ServerId);
        await Server.WaitForPauseState(groupId, expectedPaused: true);

        // Publish a job
        var publisher = Server.CreatePublisher();
        var jobId = await publisher.Enqueue(new UnitRequest());
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Wait — job should NOT be picked up (poll DB to confirm it stays Enqueued)
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            var job = await Server.GetJob(jobId);
            job.CurrentState.ShouldBe(State.Enqueued);
            await Task.Delay(200, Xunit.TestContext.Current.CancellationToken);
        }

        // Resume and let it process
        await svc.ResumeServer(Server.ServerId);
        await Server.WaitForCompletion();
    }

    [TimedFact]
    public async Task PauseServer_Resume_JobsGetProcessed()
    {
        var groupId = await GetFirstGroupId();
        var svc = Server.CreateServerCommandService();

        // Pause, publish, then resume
        await svc.PauseServer(Server.ServerId);
        await Server.WaitForPauseState(groupId, expectedPaused: true);

        var publisher = Server.CreatePublisher();
        var jobId = await publisher.Enqueue(new UnitRequest());
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Resume — job should complete
        await svc.ResumeServer(Server.ServerId);
        await Server.WaitForCompletion();

        var job = await Server.GetJob(jobId);
        job.CurrentState.ShouldBe(State.Completed);
    }

    [TimedFact]
    public async Task PauseWorkerGroup_JobsStayEnqueued()
    {
        var groupId = await GetFirstGroupId();
        var svc = Server.CreateServerCommandService();

        // Pause group and wait for propagation
        await svc.PauseWorkerGroup(groupId);
        await Server.WaitForPauseState(groupId, expectedPaused: true);

        // Publish a job
        var publisher = Server.CreatePublisher();
        var jobId = await publisher.Enqueue(new UnitRequest());
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Wait — job should NOT be picked up
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            var job = await Server.GetJob(jobId);
            job.CurrentState.ShouldBe(State.Enqueued);
            await Task.Delay(200, Xunit.TestContext.Current.CancellationToken);
        }

        // Resume and let it process
        await svc.ResumeWorkerGroup(groupId);
        await Server.WaitForCompletion();
    }

    [TimedFact]
    public async Task PauseWorkerGroup_Resume_JobsGetProcessed()
    {
        var groupId = await GetFirstGroupId();
        var svc = Server.CreateServerCommandService();

        // Pause, publish, resume
        await svc.PauseWorkerGroup(groupId);
        await Server.WaitForPauseState(groupId, expectedPaused: true);

        var publisher = Server.CreatePublisher();
        var jobId = await publisher.Enqueue(new UnitRequest());
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await svc.ResumeWorkerGroup(groupId);
        await Server.WaitForCompletion();

        var job = await Server.GetJob(jobId);
        job.CurrentState.ShouldBe(State.Completed);
    }
}
