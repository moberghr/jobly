using System.Diagnostics;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Tests.Fixtures;
using Jobly.Tests.TestData.Handlers;
using Jobly.Worker;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Notifications;

/// <summary>
/// End-to-end integration tests for AddJoblyDatabasePush on SQL Server (Service Broker).
/// Mirrors PostgresDatabasePushIntegrationTests — each test starts a fresh JoblyTestServer
/// with UseDispatcher=true, long PollingInterval, and DB push enabled, then asserts that
/// jobs are picked up quickly via push (not via the long poll).
/// </summary>
[Collection<SqlServerIntegrationCollection>]
[Trait("Category", "SqlServer")]
public class SqlServerDatabasePushIntegrationTests : IAsyncLifetime
{
    private readonly SqlServerIntegrationFixture _fixture;

    public SqlServerDatabasePushIntegrationTests(SqlServerIntegrationFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync() => await _fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [TimedFact]
    public async Task JobEnqueued_WithDispatcherPlusPush_DispatcherPicksUpWithoutPolling()
    {
        await using var server = await JoblyTestServer.StartAsync(
            fixture: _fixture,
            configure: cfg =>
            {
                cfg.UseDispatcher = true;
                cfg.PollingInterval = TimeSpan.FromSeconds(10);
                cfg.MaxPollingInterval = TimeSpan.FromSeconds(10);
                cfg.PollingIntervalFactor = 1.0;
            },
            configureServices: services => services.AddJoblyDatabasePush<TestContext>(o => o.ChannelName = "jobly_push_it_job_mssql"));

        var publisher = server.CreatePublisher();
        var jobId = await publisher.Enqueue(new CounterRequest());
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        var sw = Stopwatch.StartNew();
        await server.WaitForJobState(jobId, State.Completed, TimeSpan.FromSeconds(5));
        sw.Stop();

        sw.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(3), "Push should wake the dispatcher in <3s even though PollingInterval=10s");
    }

    [TimedFact]
    public async Task MessageEnqueued_WithPush_MessageRoutingWakesImmediately()
    {
        await using var server = await JoblyTestServer.StartAsync(
            fixture: _fixture,
            configure: cfg =>
            {
                cfg.UseDispatcher = true;
                cfg.PollingInterval = TimeSpan.FromSeconds(10);
                cfg.MaxPollingInterval = TimeSpan.FromSeconds(10);
                cfg.MessageRoutingInterval = TimeSpan.FromSeconds(10);
                cfg.PollingIntervalFactor = 1.0;
            },
            configureServices: services => services.AddJoblyDatabasePush<TestContext>(o => o.ChannelName = "jobly_push_it_msg_mssql"));

        var publisher = server.CreatePublisher();
        var messageId = await publisher.Publish(new SingleHandlerMessage());
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        var sw = Stopwatch.StartNew();
        await server.WaitForCompletion(TimeSpan.FromSeconds(8));
        sw.Stop();

        var ctx = server.CreateContext();
        var message = await ctx.Set<Job>().FirstAsync(j => j.Id == messageId, Xunit.TestContext.Current.CancellationToken);
        message.CurrentState.ShouldBe(State.Completed);

        sw.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5), "Push should wake MessageRoutingTask AND the dispatcher within 5s even though both intervals are 10s");
    }

    [TimedFact]
    public async Task PushEnabled_WithoutDispatcher_WorksButPollsForJobs()
    {
        // UseDispatcher=false + push: the listener logs a warning, JobEnqueued notifications
        // have no dispatcher to signal (individual workers still poll). But MessageRoutingTask
        // and OrchestrationTask still benefit. The test verifies the warning path doesn't crash
        // and the system still processes work correctly.
        await using var server = await JoblyTestServer.StartAsync(
            fixture: _fixture,
            configure: cfg =>
            {
                cfg.UseDispatcher = false;
                cfg.PollingInterval = TimeSpan.FromMilliseconds(200);
            },
            configureServices: services => services.AddJoblyDatabasePush<TestContext>(o => o.ChannelName = "jobly_push_it_noDispatch_mssql"));

        var publisher = server.CreatePublisher();
        var jobId = await publisher.Enqueue(new CounterRequest());
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await server.WaitForJobState(jobId, State.Completed, TimeSpan.FromSeconds(10));
    }
}
