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

    private async Task<Guid> GetFirstGroupId(WarpTestServer server)
    {
        var ctx = Fixture.CreateContext();
        var group = await ctx.Set<WorkerGroup>()
            .Where(g => g.ServerId == server.ServerId)
            .FirstAsync(Xunit.TestContext.Current.CancellationToken);
        return group.Id;
    }

    [TimedFact]
    public async Task PauseServer_PauseStateHolder_UpdatedByHeartbeat()
    {
        await using var server = await WarpTestServer.StartAsync(Fixture);
        var groupId = await GetFirstGroupId(server);
        var svc = server.CreateServerCommandService();

        await svc.PauseServer(server.ServerId);
        await server.WaitForPauseState(groupId, expectedPaused: true);
        server.PauseState.IsPaused(groupId).ShouldBeTrue();

        // Cleanup
        await svc.ResumeServer(server.ServerId);
        await server.WaitForPauseState(groupId, expectedPaused: false);
    }

    [TimedFact]
    public async Task PauseServer_JobsStayEnqueued()
    {
        await using var server = await WarpTestServer.StartAsync(Fixture);
        var groupId = await GetFirstGroupId(server);
        var svc = server.CreateServerCommandService();

        // Pause and wait for propagation
        await svc.PauseServer(server.ServerId);
        await server.WaitForPauseState(groupId, expectedPaused: true);

        // Publish a job
        var publisher = server.CreatePublisher();
        var jobId = await publisher.Enqueue(new UnitRequest());
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Wait — job should NOT be picked up (poll DB to confirm it stays Enqueued)
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            var job = await server.GetJob(jobId);
            job.CurrentState.ShouldBe(State.Enqueued);
            await Task.Delay(200, Xunit.TestContext.Current.CancellationToken);
        }

        // Resume and let it process
        await svc.ResumeServer(server.ServerId);
        await server.WaitForCompletion();
    }

    [TimedFact]
    public async Task PauseServer_Resume_JobsGetProcessed()
    {
        await using var server = await WarpTestServer.StartAsync(Fixture);
        var groupId = await GetFirstGroupId(server);
        var svc = server.CreateServerCommandService();

        // Pause, publish, then resume
        await svc.PauseServer(server.ServerId);
        await server.WaitForPauseState(groupId, expectedPaused: true);

        var publisher = server.CreatePublisher();
        var jobId = await publisher.Enqueue(new UnitRequest());
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Resume — job should complete
        await svc.ResumeServer(server.ServerId);
        await server.WaitForCompletion();

        var job = await server.GetJob(jobId);
        job.CurrentState.ShouldBe(State.Completed);
    }

    [TimedFact]
    public async Task PauseWorkerGroup_JobsStayEnqueued()
    {
        await using var server = await WarpTestServer.StartAsync(Fixture);
        var groupId = await GetFirstGroupId(server);
        var svc = server.CreateServerCommandService();

        // Pause group and wait for propagation
        await svc.PauseWorkerGroup(groupId);
        await server.WaitForPauseState(groupId, expectedPaused: true);

        // Publish a job
        var publisher = server.CreatePublisher();
        var jobId = await publisher.Enqueue(new UnitRequest());
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Wait — job should NOT be picked up
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            var job = await server.GetJob(jobId);
            job.CurrentState.ShouldBe(State.Enqueued);
            await Task.Delay(200, Xunit.TestContext.Current.CancellationToken);
        }

        // Resume and let it process
        await svc.ResumeWorkerGroup(groupId);
        await server.WaitForCompletion();
    }

    [TimedFact]
    public async Task PauseWorkerGroup_Resume_JobsGetProcessed()
    {
        await using var server = await WarpTestServer.StartAsync(Fixture);
        var groupId = await GetFirstGroupId(server);
        var svc = server.CreateServerCommandService();

        // Pause, publish, resume
        await svc.PauseWorkerGroup(groupId);
        await server.WaitForPauseState(groupId, expectedPaused: true);

        var publisher = server.CreatePublisher();
        var jobId = await publisher.Enqueue(new UnitRequest());
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await svc.ResumeWorkerGroup(groupId);
        await server.WaitForCompletion();

        var job = await server.GetJob(jobId);
        job.CurrentState.ShouldBe(State.Completed);
    }
}
