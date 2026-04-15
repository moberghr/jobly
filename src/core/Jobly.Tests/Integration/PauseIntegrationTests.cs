using Jobly.Core.Data.Entities;
using Jobly.Core.Enums;
using Jobly.Tests.Fixtures;
using Jobly.Tests.TestData.Handlers;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Integration;

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
            .FirstAsync();
        return group.Id;
    }

    [Fact]
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

    [Fact]
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
        await publisher.SaveChangesAsync();

        // Wait — job should NOT be picked up (poll DB to confirm it stays Enqueued)
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            var job = await Server.GetJob(jobId);
            job.CurrentState.ShouldBe(State.Enqueued);
            await Task.Delay(200);
        }

        // Resume and let it process
        await svc.ResumeServer(Server.ServerId);
        await Server.WaitForCompletion();
    }

    [Fact]
    public async Task PauseServer_Resume_JobsGetProcessed()
    {
        var groupId = await GetFirstGroupId();
        var svc = Server.CreateServerCommandService();

        // Pause, publish, then resume
        await svc.PauseServer(Server.ServerId);
        await Server.WaitForPauseState(groupId, expectedPaused: true);

        var publisher = Server.CreatePublisher();
        var jobId = await publisher.Enqueue(new UnitRequest());
        await publisher.SaveChangesAsync();

        // Resume — job should complete
        await svc.ResumeServer(Server.ServerId);
        await Server.WaitForCompletion();

        var job = await Server.GetJob(jobId);
        job.CurrentState.ShouldBe(State.Completed);
    }

    [Fact]
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
        await publisher.SaveChangesAsync();

        // Wait — job should NOT be picked up
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            var job = await Server.GetJob(jobId);
            job.CurrentState.ShouldBe(State.Enqueued);
            await Task.Delay(200);
        }

        // Resume and let it process
        await svc.ResumeWorkerGroup(groupId);
        await Server.WaitForCompletion();
    }

    [Fact]
    public async Task PauseWorkerGroup_Resume_JobsGetProcessed()
    {
        var groupId = await GetFirstGroupId();
        var svc = Server.CreateServerCommandService();

        // Pause, publish, resume
        await svc.PauseWorkerGroup(groupId);
        await Server.WaitForPauseState(groupId, expectedPaused: true);

        var publisher = Server.CreatePublisher();
        var jobId = await publisher.Enqueue(new UnitRequest());
        await publisher.SaveChangesAsync();

        await svc.ResumeWorkerGroup(groupId);
        await Server.WaitForCompletion();

        var job = await Server.GetJob(jobId);
        job.CurrentState.ShouldBe(State.Completed);
    }
}

[Collection<PostgreSqlIntegrationCollection>]
public class PauseIntegrationTests_PostgreSql : PauseIntegrationTestsBase
{
    public PauseIntegrationTests_PostgreSql(PostgreSqlIntegrationFixture fixture)
        : base(fixture)
    {
    }
}

[Collection<SqlServerIntegrationCollection>]
[Trait("Category", "SqlServer")]
public class PauseIntegrationTests_SqlServer : PauseIntegrationTestsBase
{
    public PauseIntegrationTests_SqlServer(SqlServerIntegrationFixture fixture)
        : base(fixture)
    {
    }
}
