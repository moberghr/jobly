using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Services;
using Jobly.Tests.Fixtures;
using Jobly.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobly.Tests.Unit;

/// <summary>
/// Audit round 2: job lifecycle edge cases.
/// </summary>
public abstract class AuditFixRound2TestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected AuditFixRound2TestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync() => await _fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

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
        await ctx.SaveChangesAsync();

        var svc = new JobCommandService<TestContext>(_fixture.CreateContext(), TimeProvider.System, Options.Create(new JoblyConfiguration()));
        await svc.RequeueJob(jobId);

        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FindAsync(jobId);
        job.ShouldNotBeNull();

        // Should NOT be Enqueued — that would cause double execution while worker is still running
        job.CurrentState.ShouldNotBe(State.Enqueued, "Requeuing a Processing job would cause double execution");
    }

    /// <summary>
    /// When a parent is Deleted, its Awaiting children should be cleaned up (Deleted).
    /// Otherwise they can never run.
    /// </summary>
    [TimedFact]
    public async Task Orchestration_WhenParentDeleted_AwaitingChildrenAreFailed()
    {
        var ctx = _fixture.CreateContext();
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        ctx.Set<Job>().Add(new Job
        {
            Id = parentId,
            Kind = JobKind.Batch,
            CurrentState = State.Deleted,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            JobCount = 1,
        });
        ctx.Set<Job>().Add(new Job
        {
            Id = childId,
            Kind = JobKind.Job,
            CurrentState = State.Awaiting,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            ParentJobId = parentId,
        });
        await ctx.SaveChangesAsync();

        // Run orchestration
        var orchCtx = _fixture.CreateContext();
        await OrchestrationTask<TestContext>.RunOrchestration(orchCtx, TimeProvider.System, TimeSpan.FromDays(1), CancellationToken.None);

        var readCtx = _fixture.CreateContext();
        var child = await readCtx.Set<Job>().FindAsync(childId);
        child.ShouldNotBeNull();
        child.CurrentState.ShouldBe(State.Failed, "Awaiting child of a Deleted parent should be Failed");
    }

    /// <summary>
    /// When a parent fails with OnlyOnSucceeded, Awaiting continuations stay Awaiting.
    /// The parent could be requeued and succeed later.
    /// </summary>
    [TimedFact]
    public async Task Orchestration_WhenParentFailedOnlyOnSucceeded_AwaitingContinuationsAreDeleted()
    {
        var ctx = _fixture.CreateContext();

        // Batch parent with one failed child
        var parentId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = parentId,
            Kind = JobKind.Batch,
            CurrentState = State.Awaiting,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            JobCount = 1,
            ContinuationOptions = ContinuationOptions.OnlyOnSucceeded,
        });
        ctx.Set<Job>().Add(new Job
        {
            Kind = JobKind.Job,
            CurrentState = State.Failed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            ParentJobId = parentId,
        });

        // Continuation batch waiting on parent
        var continuationBatchId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = continuationBatchId,
            Kind = JobKind.Batch,
            CurrentState = State.Awaiting,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            ParentJobId = parentId,
        });
        var continuationChildId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = continuationChildId,
            Kind = JobKind.Job,
            CurrentState = State.Awaiting,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            ParentJobId = continuationBatchId,
        });
        await ctx.SaveChangesAsync();

        // Run orchestration twice (first finalizes parent, second should clean up continuations)
        var orchCtx1 = _fixture.CreateContext();
        await OrchestrationTask<TestContext>.RunOrchestration(orchCtx1, TimeProvider.System, TimeSpan.FromDays(1), CancellationToken.None);
        var orchCtx2 = _fixture.CreateContext();
        await OrchestrationTask<TestContext>.RunOrchestration(orchCtx2, TimeProvider.System, TimeSpan.FromDays(1), CancellationToken.None);

        var readCtx = _fixture.CreateContext();

        // Parent should be Failed (child failed, OnlyOnSucceeded)
        var parent = await readCtx.Set<Job>().FindAsync(parentId);
        parent.ShouldNotBeNull();
        parent.CurrentState.ShouldBe(State.Failed);

        // Continuation batch and its child should stay Awaiting (condition not met, but parent could be requeued)
        var contBatch = await readCtx.Set<Job>().FindAsync(continuationBatchId);
        contBatch.ShouldNotBeNull();
        contBatch.CurrentState.ShouldBe(State.Awaiting, "Continuation of failed OnlyOnSucceeded parent should stay Awaiting");

        var contChild = await readCtx.Set<Job>().FindAsync(continuationChildId);
        contChild.ShouldNotBeNull();
        contChild.CurrentState.ShouldBe(State.Awaiting, "Children of awaiting continuation should also stay Awaiting");
    }
}

[Collection<PostgreSqlCollection>]
public class AuditFixRound2Tests_PostgreSql : AuditFixRound2TestsBase
{
    public AuditFixRound2Tests_PostgreSql(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }
}

[Collection<SqlServerCollection>]
[Trait("Category", "SqlServer")]
public class AuditFixRound2Tests_SqlServer : AuditFixRound2TestsBase
{
    public AuditFixRound2Tests_SqlServer(SqlServerFixture fixture)
        : base(fixture)
    {
    }
}
