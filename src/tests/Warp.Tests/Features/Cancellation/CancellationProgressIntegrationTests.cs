using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Warp.Core.Data.Entities;
using Warp.Core.Enums;
using Warp.Tests.Fixtures;
using Warp.Tests.TestData.Handlers;
using Warp.Worker;

namespace Warp.Tests.Features.Cancellation;

[GenerateDatabaseTests]
public abstract class CancellationProgressIntegrationTestsBase : IntegrationTestBase
{
    protected CancellationProgressIntegrationTestsBase(IDatabaseFixture fixture)
        : base(fixture)
    {
    }

    [TimedFact]
    public async Task GivenHandlerThatReportsProgressThenIsCancelled_WhenCancelled_ThenReportedProgressRowsSurvive()
    {
        var barrier = new BarrierSignal();
        await using var server = await WarpTestServer.StartAsync(
            Fixture,
            cfg => cfg.Services.AddSingleton(barrier));

        var publisher = server.CreatePublisher();
        var jobId = await publisher.Enqueue(new CancellableProgressRequest());
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Deterministic handshake: the handler signals Running only AFTER both ReportProgress
        // calls have executed. No race window — when this returns, progress is in the collector
        // and the handler is parked on signal.CanFinish.
        await barrier.Running.WaitAsync(Xunit.TestContext.Current.CancellationToken);

        // Cancel mid-flight. cancellationToken on signal.CanFinish.WaitAsync trips, handler
        // exits via OperationCanceledException, worker enters cancellation branch which drains
        // progress in the same SaveChangesAsync as the "Cancelled" JobLog row.
        var cmd = server.CreateCommandService();
        await cmd.DeleteJob(jobId);

        await server.WaitForJobLog(jobId, "Cancelled", timeout: TimeSpan.FromSeconds(5));

        var ctx = Fixture.CreateContext();
        var progressRows = await ctx.Set<JobLog>()
            .AsNoTracking()
            .Where(x => x.JobId == jobId)
            .Where(x => x.EventType == "Progress")
            .ToListAsync(Xunit.TestContext.Current.CancellationToken);

        // Only the final value per bar survives (dedup-on-no-change in JobProgressCollector).
        progressRows.Count.ShouldBe(1);
        progressRows[0].Name.ShouldBe("phase");
        progressRows[0].Value.ShouldBe((short)50);

        var job = await server.GetJob(jobId);
        job.CurrentState.ShouldBe(State.Deleted);
    }

    [TimedFact]
    public async Task GivenDispatcherMode_WhenHandlerReportsProgressThenIsCancelled_ThenReportedProgressRowsSurvive()
    {
        var barrier = new BarrierSignal();
        await using var server = await WarpTestServer.StartAsync(
            Fixture,
            cfg =>
            {
                cfg.UseDispatcher = true;
                cfg.WorkerCount = 2;
                cfg.CompletionBatchSize = 10;
                cfg.CompletionFlushInterval = TimeSpan.FromMilliseconds(50);
            },
            services => services.AddSingleton(barrier));

        var publisher = server.CreatePublisher();
        var jobId = await publisher.Enqueue(new CancellableProgressRequest());
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Same deterministic handshake as single-worker mode — handler signals Running only
        // after both ReportProgress calls have executed.
        await barrier.Running.WaitAsync(Xunit.TestContext.Current.CancellationToken);

        var cmd = server.CreateCommandService();
        await cmd.DeleteJob(jobId);

        // In dispatcher mode the terminal commit is batched (CompletionBatchSize=10,
        // CompletionFlushInterval=50ms). WaitForJobLog polls until "Cancelled" appears,
        // which only happens after the batch flush commits the entire PendingCompletion
        // (including the drained progress rows from CollectLogs).
        await server.WaitForJobLog(jobId, "Cancelled", timeout: TimeSpan.FromSeconds(10));

        var ctx = Fixture.CreateContext();
        var progressRows = await ctx.Set<JobLog>()
            .AsNoTracking()
            .Where(x => x.JobId == jobId)
            .Where(x => x.EventType == "Progress")
            .ToListAsync(Xunit.TestContext.Current.CancellationToken);

        progressRows.Count.ShouldBe(1);
        progressRows[0].Name.ShouldBe("phase");
        progressRows[0].Value.ShouldBe((short)50);

        var job = await server.GetJob(jobId);
        job.CurrentState.ShouldBe(State.Deleted);
    }
}
