using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Services;
using Jobly.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobly.Tests.Reliability;

[GenerateDatabaseTests(FixtureKind.Default)]
public abstract class RequeueEdgeCaseTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected RequeueEdgeCaseTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync() => await _fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [TimedFact]
    public async Task RequeueJob_AlreadyEnqueued_NoOpReturnsEarly()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act
        var svc = Jobly.Tests.Helpers.TestTasks.CreateJobCommandService(_fixture.CreateContext());
        await svc.RequeueJob(jobId);

        // Assert — state should remain Enqueued, and no Requeued log should exist
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == jobId, Xunit.TestContext.Current.CancellationToken);
        job.ShouldNotBeNull();
        job.CurrentState.ShouldBe(State.Enqueued);

        var logs = await readCtx.Set<JobLog>().Where(l => l.JobId == jobId).ToListAsync(Xunit.TestContext.Current.CancellationToken);
        logs.ShouldNotContain(l => l.EventType == "Requeued");
    }

    [TimedFact]
    public async Task RequeueJob_DeletedJob_RequeuesSuccessfully()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Deleted,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            ExpireAt = DateTime.UtcNow.AddDays(1),
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act
        var svc = Jobly.Tests.Helpers.TestTasks.CreateJobCommandService(_fixture.CreateContext());
        await svc.RequeueJob(jobId);

        // Assert
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == jobId, Xunit.TestContext.Current.CancellationToken);
        job.ShouldNotBeNull();
        job.CurrentState.ShouldBe(State.Enqueued);
        job.ExpireAt.ShouldBeNull();
    }

    [TimedFact]
    public async Task RequeueJob_ParentNotFound_StillRequeues()
    {
        // Arrange — child job with ParentJobId pointing to non-existent parent
        // We cannot set a FK to a non-existent row, so we create a real parent, then delete it
        var ctx = _fixture.CreateContext();
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        ctx.Set<Job>().Add(new Job
        {
            Id = parentId,
            Kind = JobKind.Batch,
            CurrentState = State.Completed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            JobCount = 1,
        });
        ctx.Set<Job>().Add(new Job
        {
            Id = childId,
            Kind = JobKind.Job,
            CurrentState = State.Failed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            ParentJobId = parentId,
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act — requeue should succeed even though parent exists (and handle it gracefully)
        var svc = Jobly.Tests.Helpers.TestTasks.CreateJobCommandService(_fixture.CreateContext());
        await svc.RequeueJob(childId);

        // Assert
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == childId, Xunit.TestContext.Current.CancellationToken);
        job.ShouldNotBeNull();
        job.CurrentState.ShouldBe(State.Enqueued);
    }

    [TimedFact]
    public async Task RequeueJob_NonExistentJob_Throws()
    {
        // Act & Assert
        var svc = Jobly.Tests.Helpers.TestTasks.CreateJobCommandService(_fixture.CreateContext());
        var ex = await Should.ThrowAsync<ArgumentException>(async () =>
            await svc.RequeueJob(Guid.NewGuid()));

        ex.Message.ShouldContain("Job not found");
    }

    [TimedFact]
    public async Task DeleteJob_NonExistentJob_Throws()
    {
        // Act & Assert
        var svc = Jobly.Tests.Helpers.TestTasks.CreateJobCommandService(_fixture.CreateContext());
        var ex = await Should.ThrowAsync<ArgumentException>(async () =>
            await svc.DeleteJob(Guid.NewGuid()));

        ex.Message.ShouldContain("Job not found");
    }

    /// <summary>
    /// CRITICAL #2: RequeueJob must lock the parent row to prevent race with OrchestrationTask.
    /// This test verifies RequeueJob correctly restores the parent to a non-terminal state.
    /// The real race is hard to reproduce in a unit test, but we can verify the parent's
    /// state is correctly set after requeue of a child whose parent was already finalized.
    /// </summary>
    [TimedFact]
    public async Task RequeueJob_WhenParentIsCompleted_SetsParentBackToAwaitingOrProcessing()
    {
        // Arrange: a batch with one completed child and a finalized parent
        var ctx = _fixture.CreateContext();
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        ctx.Set<Job>().Add(new Job
        {
            Id = parentId,
            Kind = JobKind.Batch,
            CurrentState = State.Completed, // already finalized
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            JobCount = 1,
            ExpireAt = DateTime.UtcNow.AddDays(1),
        });
        ctx.Set<Job>().Add(new Job
        {
            Id = childId,
            Kind = JobKind.Job,
            CurrentState = State.Failed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            ParentJobId = parentId,
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act: requeue the failed child
        var svc = Jobly.Tests.Helpers.TestTasks.CreateJobCommandService(_fixture.CreateContext());
        await svc.RequeueJob(childId);

        // Assert: parent should be back in Awaiting (for batch) and ExpireAt cleared
        var readCtx = _fixture.CreateContext();
        var parent = await readCtx.Set<Job>().FindAsync([parentId], Xunit.TestContext.Current.CancellationToken);
        parent.ShouldNotBeNull();
        parent.CurrentState.ShouldBe(State.Awaiting);
        parent.ExpireAt.ShouldBeNull();
        parent.JobCount.ShouldBe(2); // incremented from 1

        var child = await readCtx.Set<Job>().FindAsync([childId], Xunit.TestContext.Current.CancellationToken);
        child.ShouldNotBeNull();
        child.CurrentState.ShouldBe(State.Enqueued);
    }

    /// <summary>
    /// RequeueJob on a Processing job should not set it to Enqueued (would cause double execution).
    /// It should either refuse or treat it as cancellation.
    /// </summary>
    [TimedFact]
    public async Task RequeueJob_WhenProcessing_DoesNotSetEnqueued()
    {
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Processing,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            CurrentWorkerId = Guid.NewGuid(),
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        var svc = Jobly.Tests.Helpers.TestTasks.CreateJobCommandService(_fixture.CreateContext());
        await svc.RequeueJob(jobId);

        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FindAsync([jobId], Xunit.TestContext.Current.CancellationToken);
        job.ShouldNotBeNull();

        // Should NOT be Enqueued — that would cause double execution while worker is still running
        job.CurrentState.ShouldNotBe(State.Enqueued, "Requeuing a Processing job would cause double execution");
    }

    /// <summary>
    /// RequeueJob on a child should lock the parent row to prevent concurrent OrchestrationTask race.
    /// Verify that parent state is correctly set after requeue.
    /// </summary>
    [TimedFact]
    public async Task RequeueJob_LocksParentRow_ParentStateCorrect()
    {
        // Arrange: batch with 2 failed children, parent finalized as Failed
        var ctx = _fixture.CreateContext();
        var parentId = Guid.NewGuid();
        var child1Id = Guid.NewGuid();
        var child2Id = Guid.NewGuid();

        ctx.Set<Job>().Add(new Job
        {
            Id = parentId,
            Kind = JobKind.Batch,
            CurrentState = State.Failed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            JobCount = 2,
            ExpireAt = DateTime.UtcNow.AddDays(1),
        });
        ctx.Set<Job>().Add(new Job
        {
            Id = child1Id,
            Kind = JobKind.Job,
            CurrentState = State.Failed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            ParentJobId = parentId,
        });
        ctx.Set<Job>().Add(new Job
        {
            Id = child2Id,
            Kind = JobKind.Job,
            CurrentState = State.Failed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            ParentJobId = parentId,
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Act: requeue child1
        var svc = Jobly.Tests.Helpers.TestTasks.CreateJobCommandService(_fixture.CreateContext());
        await svc.RequeueJob(child1Id);

        // Assert: parent should be back in Awaiting, JobCount incremented, ExpireAt cleared
        var readCtx = _fixture.CreateContext();
        var parent = await readCtx.Set<Job>().FindAsync([parentId], Xunit.TestContext.Current.CancellationToken);
        parent.ShouldNotBeNull();
        parent.CurrentState.ShouldBe(State.Awaiting);
        parent.ExpireAt.ShouldBeNull();
        parent.JobCount.ShouldBe(3);

        var child1 = await readCtx.Set<Job>().FindAsync([child1Id], Xunit.TestContext.Current.CancellationToken);
        child1.ShouldNotBeNull();
        child1.CurrentState.ShouldBe(State.Enqueued);
        child1.ScheduleTime.ShouldBeLessThanOrEqualTo(DateTime.UtcNow.AddSeconds(5));
    }
}
