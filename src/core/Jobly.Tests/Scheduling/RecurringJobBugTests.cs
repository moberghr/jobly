using System.Text.Json;
using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Services;
using Jobly.Tests.Fixtures;
using Jobly.Tests.TestData.Handlers;
using Jobly.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobly.Tests.Scheduling;

[GenerateDatabaseTests(FixtureKind.Default)]
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
        var jobs = await readCtx.Set<Job>().Where(j => j.Kind == JobKind.Job).ToListAsync();
        jobs.Count.ShouldBe(0, "AddOrUpdateRecurringJob should not create any jobs");

        var recurring = await readCtx.Set<RecurringJob>().FirstOrDefaultAsync(r => r.Name == "no-job-test");
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
        var recurring = await setupCtx.Set<RecurringJob>().FirstAsync(r => r.Name == "schedule-now-test");
        recurring.NextExecution = DateTime.UtcNow.AddMinutes(-5);
        await setupCtx.SaveChangesAsync();

        // Act
        var schedCtx = _fixture.CreateContext();
        await RecurringJobSchedulerTask<TestContext>.ScheduleRecurringJobs(schedCtx, TimeProvider.System);

        // Assert: the created job should have ScheduleTime <= now (ready for execution)
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().Where(j => j.Kind == JobKind.Job).FirstOrDefaultAsync();
        job.ShouldNotBeNull();
        job.ScheduleTime.ShouldBeLessThanOrEqualTo(DateTime.UtcNow, "Job should be ready for immediate execution");

        // NextExecution should be updated to a future time
        var updatedRecurring = await readCtx.Set<RecurringJob>().FirstAsync(r => r.Name == "schedule-now-test");
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
        await ctx.SaveChangesAsync();

        // Act
        var svc = new JobCommandService<TestContext>(_fixture.CreateContext(), TimeProvider.System, Options.Create(new JoblyConfiguration()));
        await svc.RequeueJob(jobId);

        // Assert: ScheduleTime should be reset to now (not stay in the future)
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FindAsync(jobId);
        job.ShouldNotBeNull();
        job.CurrentState.ShouldBe(State.Enqueued);
        job.ScheduleTime.ShouldBeLessThanOrEqualTo(DateTime.UtcNow.AddSeconds(5), "Requeued job should execute immediately, not stay future-scheduled");
    }
}
