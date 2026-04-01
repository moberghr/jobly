using Jobly.Core.Enums;
using Jobly.Tests.Fixtures;
using Jobly.Tests.TestData.Handlers;
using Shouldly;

namespace Jobly.Tests.Integration;

public abstract class CancellationIntegrationTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;
    protected JoblyTestServer _server = null!;

    protected CancellationIntegrationTestsBase(IDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetAsync();
        _server = await JoblyTestServer.StartAsync(_fixture);
    }

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
    }

    [Fact]
    public async Task GivenProcessingJob_WhenDeleted_ThenHandlerIsCancelledAndLoggedAsCancelled()
    {
        var publisher = _server.CreatePublisher();
        var jobId = await publisher.Enqueue(new CancellableRequest());
        await publisher.SaveChangesAsync();

        // Wait for worker to pick it up
        await _server.WaitForJobState(jobId, State.Processing);

        // Cancel it (delete while processing)
        var cmd = _server.CreateCommandService();
        await cmd.DeleteJob(jobId);

        // Worker should detect state change, cancel handler, and log "Cancelled"
        // Wait for the cancellation log (not just the state change, which happens immediately from DeleteJob)
        await _server.WaitForJobLog(jobId, "Cancelled", timeout: TimeSpan.FromSeconds(15));

        var job = await _server.GetJob(jobId);
        job.CurrentState.ShouldBe(State.Deleted);
    }

    [Fact]
    public async Task GivenProcessingJob_WhenDeleted_ThenCompletesQuicklyNotAfterFullDuration()
    {
        var publisher = _server.CreatePublisher();
        var jobId = await publisher.Enqueue(new CancellableRequest());
        await publisher.SaveChangesAsync();

        await _server.WaitForJobState(jobId, State.Processing);

        var cmd = _server.CreateCommandService();
        await cmd.DeleteJob(jobId);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await _server.WaitForJobState(jobId, State.Deleted, timeout: TimeSpan.FromSeconds(15));
        sw.Stop();

        // Should complete within a few seconds (CancellationCheckInterval=1s)
        // NOT the full 30s of CancellableRequest
        sw.Elapsed.TotalSeconds.ShouldBeLessThan(10);
    }
}

[Collection("PostgreSql")]
public class CancellationIntegrationTests_PostgreSql : CancellationIntegrationTestsBase
{
    public CancellationIntegrationTests_PostgreSql(PostgreSqlFixture fixture) : base(fixture) { }
}

[Collection("SqlServer")]
[Trait("Category", "SqlServer")]
public class CancellationIntegrationTests_SqlServer : CancellationIntegrationTestsBase
{
    public CancellationIntegrationTests_SqlServer(SqlServerFixture fixture) : base(fixture) { }
}
