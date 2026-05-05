using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shouldly;
using Warp.Core;
using Warp.Core.Data.Entities;
using Warp.Core.Entities;
using Warp.Core.Enums;
using Warp.Core.Services;
using Warp.Tests.Fixtures;
using Warp.Tests.TestData.Handlers;
using Warp.Worker.Services;

namespace Warp.Tests.Scheduling;

[GenerateDatabaseTests]
public abstract class RecurringJobBugTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected RecurringJobBugTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync() => await _fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [TimedFact]
    public async Task AddOrUpdateRecurringJob_DoesNotCreateJob()
    {
        var ctx = _fixture.CreateContext();
        var publisher = new RecurringJobPublisher<TestContext>(ctx, TimeProvider.System, new FakeLockProvider());

        await publisher.AddOrUpdateRecurringJob(new UnitRequest(), "no-job-test", "* * * * *");

        var readCtx = _fixture.CreateContext();
        var jobs = await readCtx.Set<Job>().Where(j => j.Kind == JobKind.Job).ToListAsync(Xunit.TestContext.Current.CancellationToken);
        jobs.Count.ShouldBe(0, "AddOrUpdateRecurringJob should not create any jobs");

        var recurring = await readCtx.Set<RecurringJob>().FirstOrDefaultAsync(r => r.Name == "no-job-test", Xunit.TestContext.Current.CancellationToken);
        recurring.ShouldNotBeNull();
        recurring.NextExecution.ShouldNotBeNull();
    }

    [TimedFact]
    public async Task ScheduleRecurringJobs_CreatesJobWithScheduleTimeNow()
    {
        // Arrange: register a recurring job with NextExecution in the past (so scheduler triggers)
        var ctx = _fixture.CreateContext();
        var publisher = new RecurringJobPublisher<TestContext>(ctx, TimeProvider.System, new FakeLockProvider());
        await publisher.AddOrUpdateRecurringJob(new UnitRequest(), "schedule-now-test", "* * * * *");

        // Set NextExecution to the past so the scheduler picks it up
        var setupCtx = _fixture.CreateContext();
        var recurring = await setupCtx.Set<RecurringJob>().FirstAsync(r => r.Name == "schedule-now-test", Xunit.TestContext.Current.CancellationToken);
        recurring.NextExecution = DateTime.UtcNow.AddMinutes(-5);
        await setupCtx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act
        var schedCtx = _fixture.CreateContext();
        await Warp.Tests.Helpers.TestTasks.CreateRecurringJobScheduler(schedCtx, TimeProvider.System).ScheduleRecurringJobsAsync(CancellationToken.None);

        // Assert: the created job should have ScheduleTime <= now (ready for execution)
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().Where(j => j.Kind == JobKind.Job).FirstOrDefaultAsync(Xunit.TestContext.Current.CancellationToken);
        job.ShouldNotBeNull();
        job.ScheduleTime.ShouldBeLessThanOrEqualTo(DateTime.UtcNow, "Job should be ready for immediate execution");

        // NextExecution should be updated to a future time
        var updatedRecurring = await readCtx.Set<RecurringJob>().FirstAsync(r => r.Name == "schedule-now-test", Xunit.TestContext.Current.CancellationToken);
        updatedRecurring.NextExecution.ShouldNotBeNull();
        updatedRecurring.NextExecution.Value.ShouldBeGreaterThan(DateTime.UtcNow, "NextExecution should be in the future");
    }

    [TimedFact]
    public async Task RequeueJob_SetsScheduleTimeToNow()
    {
        // Arrange: create a job with a future schedule time that has failed
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Failed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow.AddHours(5), // future
            Queue = "default",
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act
        var svc = Warp.Tests.Helpers.TestTasks.CreateJobCommandService(_fixture.CreateContext());
        await svc.RequeueJob(jobId);

        // Assert: ScheduleTime should be reset to now (not stay in the future)
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FindAsync([jobId], Xunit.TestContext.Current.CancellationToken);
        job.ShouldNotBeNull();
        job.CurrentState.ShouldBe(State.Enqueued);
        job.ScheduleTime.ShouldBeLessThanOrEqualTo(DateTime.UtcNow.AddSeconds(5), "Requeued job should execute immediately, not stay future-scheduled");
    }
}
