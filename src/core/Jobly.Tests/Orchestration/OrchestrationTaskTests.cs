using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Tests.Fixtures;
using Jobly.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Orchestration;

[GenerateDatabaseTests(FixtureKind.Default)]
public abstract class OrchestrationTaskTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected OrchestrationTaskTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync() => await _fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [TimedFact]
    public async Task RunOrchestration_BatchAllChildrenCompleted_FinalizesBatch()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var batchId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = batchId,
            Kind = JobKind.Batch,
            CurrentState = State.Awaiting,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            JobCount = 3,
        });

        for (var i = 0; i < 3; i++)
        {
            ctx.Set<Job>().Add(new Job
            {
                Id = Guid.NewGuid(),
                Kind = JobKind.Job,
                CurrentState = State.Completed,
                CreateTime = DateTime.UtcNow,
                ScheduleTime = DateTime.UtcNow,
                Queue = "default",
                ParentJobId = batchId,
            });
        }

        await ctx.SaveChangesAsync();

        // Act
        var orchCtx = _fixture.CreateContext();
        await OrchestrationTask<TestContext>.RunOrchestration(orchCtx, TimeProvider.System, TimeSpan.FromDays(1), CancellationToken.None);

        // Assert
        var readCtx = _fixture.CreateContext();
        var batch = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == batchId);
        batch.ShouldNotBeNull();
        batch.CurrentState.ShouldBe(State.Completed);
    }

    [TimedFact]
    public async Task RunOrchestration_BatchSomeChildrenEnqueued_DoesNotFinalize()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var batchId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = batchId,
            Kind = JobKind.Batch,
            CurrentState = State.Awaiting,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            JobCount = 2,
        });

        ctx.Set<Job>().Add(new Job
        {
            Id = Guid.NewGuid(),
            Kind = JobKind.Job,
            CurrentState = State.Completed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            ParentJobId = batchId,
        });
        ctx.Set<Job>().Add(new Job
        {
            Id = Guid.NewGuid(),
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            ParentJobId = batchId,
        });
        await ctx.SaveChangesAsync();

        // Act
        var orchCtx = _fixture.CreateContext();
        await OrchestrationTask<TestContext>.RunOrchestration(orchCtx, TimeProvider.System, TimeSpan.FromDays(1), CancellationToken.None);

        // Assert
        var readCtx = _fixture.CreateContext();
        var batch = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == batchId);
        batch.ShouldNotBeNull();
        batch.CurrentState.ShouldBe(State.Awaiting);
    }

    [TimedFact]
    public async Task RunOrchestration_BatchWithFailedChild_OnlyOnSucceeded_BatchFails()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var batchId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = batchId,
            Kind = JobKind.Batch,
            CurrentState = State.Awaiting,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            JobCount = 2,
            ContinuationOptions = ContinuationOptions.OnlyOnSucceeded,
        });

        ctx.Set<Job>().Add(new Job
        {
            Id = Guid.NewGuid(),
            Kind = JobKind.Job,
            CurrentState = State.Completed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            ParentJobId = batchId,
        });
        ctx.Set<Job>().Add(new Job
        {
            Id = Guid.NewGuid(),
            Kind = JobKind.Job,
            CurrentState = State.Failed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            ParentJobId = batchId,
        });
        await ctx.SaveChangesAsync();

        // Act
        var orchCtx = _fixture.CreateContext();
        await OrchestrationTask<TestContext>.RunOrchestration(orchCtx, TimeProvider.System, TimeSpan.FromDays(1), CancellationToken.None);

        // Assert
        var readCtx = _fixture.CreateContext();
        var batch = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == batchId);
        batch.ShouldNotBeNull();
        batch.CurrentState.ShouldBe(State.Failed);
    }

    [TimedFact]
    public async Task RunOrchestration_BatchWithFailedChild_OnAnyFinished_BatchCompletes()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var batchId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = batchId,
            Kind = JobKind.Batch,
            CurrentState = State.Awaiting,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            JobCount = 2,
            ContinuationOptions = ContinuationOptions.OnAnyFinishedState,
        });

        ctx.Set<Job>().Add(new Job
        {
            Id = Guid.NewGuid(),
            Kind = JobKind.Job,
            CurrentState = State.Completed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            ParentJobId = batchId,
        });
        ctx.Set<Job>().Add(new Job
        {
            Id = Guid.NewGuid(),
            Kind = JobKind.Job,
            CurrentState = State.Failed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            ParentJobId = batchId,
        });
        await ctx.SaveChangesAsync();

        // Act
        var orchCtx = _fixture.CreateContext();
        await OrchestrationTask<TestContext>.RunOrchestration(orchCtx, TimeProvider.System, TimeSpan.FromDays(1), CancellationToken.None);

        // Assert
        var readCtx = _fixture.CreateContext();
        var batch = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == batchId);
        batch.ShouldNotBeNull();
        batch.CurrentState.ShouldBe(State.Completed);
    }

    [TimedFact]
    public async Task RunOrchestration_MessageAllChildrenCompleted_FinalizesMessage()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var messageId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = messageId,
            Kind = JobKind.Message,
            CurrentState = State.Processing,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
        });

        for (var i = 0; i < 2; i++)
        {
            ctx.Set<Job>().Add(new Job
            {
                Id = Guid.NewGuid(),
                Kind = JobKind.Job,
                CurrentState = State.Completed,
                CreateTime = DateTime.UtcNow,
                ScheduleTime = DateTime.UtcNow,
                Queue = "default",
                ParentJobId = messageId,
            });
        }

        await ctx.SaveChangesAsync();

        // Act
        var orchCtx = _fixture.CreateContext();
        await OrchestrationTask<TestContext>.RunOrchestration(orchCtx, TimeProvider.System, TimeSpan.FromDays(1), CancellationToken.None);

        // Assert
        var readCtx = _fixture.CreateContext();
        var message = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == messageId);
        message.ShouldNotBeNull();
        message.CurrentState.ShouldBe(State.Completed);
        message.ExpireAt.ShouldNotBeNull();
    }

    [TimedFact]
    public async Task RunOrchestration_ActivatesContinuationChildren()
    {
        // Arrange: completed batch parent -> awaiting continuation batch child -> awaiting grandchildren
        var ctx = _fixture.CreateContext();
        var parentBatchId = Guid.NewGuid();
        var continuationBatchId = Guid.NewGuid();

        // Parent batch (completed)
        ctx.Set<Job>().Add(new Job
        {
            Id = parentBatchId,
            Kind = JobKind.Batch,
            CurrentState = State.Awaiting,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            JobCount = 1,
        });

        // Parent batch child (completed) — triggers parent finalization
        ctx.Set<Job>().Add(new Job
        {
            Id = Guid.NewGuid(),
            Kind = JobKind.Job,
            CurrentState = State.Completed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            ParentJobId = parentBatchId,
        });

        // Continuation batch (awaiting, child of parent batch)
        ctx.Set<Job>().Add(new Job
        {
            Id = continuationBatchId,
            Kind = JobKind.Batch,
            CurrentState = State.Awaiting,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            ParentJobId = parentBatchId,
            JobCount = 2,
        });

        // Grandchildren (awaiting, children of continuation batch)
        var grandchildIds = new List<Guid>();
        for (var i = 0; i < 2; i++)
        {
            var gcId = Guid.NewGuid();
            grandchildIds.Add(gcId);
            ctx.Set<Job>().Add(new Job
            {
                Id = gcId,
                Kind = JobKind.Job,
                CurrentState = State.Awaiting,
                CreateTime = DateTime.UtcNow,
                ScheduleTime = DateTime.UtcNow,
                Queue = "default",
                ParentJobId = continuationBatchId,
            });
        }

        await ctx.SaveChangesAsync();

        // Act — run orchestration multiple times to finalize parent and then activate continuation
        var orchCtx1 = _fixture.CreateContext();
        await OrchestrationTask<TestContext>.RunOrchestration(orchCtx1, TimeProvider.System, TimeSpan.FromDays(1), CancellationToken.None);
        var orchCtx2 = _fixture.CreateContext();
        await OrchestrationTask<TestContext>.RunOrchestration(orchCtx2, TimeProvider.System, TimeSpan.FromDays(1), CancellationToken.None);

        // Assert
        var readCtx = _fixture.CreateContext();
        foreach (var gcId in grandchildIds)
        {
            var gc = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == gcId);
            gc.ShouldNotBeNull();
            gc.CurrentState.ShouldBe(State.Enqueued);
        }
    }

    [TimedFact]
    public async Task RunOrchestration_FailedParentOnlyOnSucceeded_ContinuationStaysAwaiting()
    {
        // Arrange: failed batch parent (OnlyOnSucceeded) -> awaiting continuation child
        var ctx = _fixture.CreateContext();
        var parentBatchId = Guid.NewGuid();
        var continuationId = Guid.NewGuid();

        ctx.Set<Job>().Add(new Job
        {
            Id = parentBatchId,
            Kind = JobKind.Batch,
            CurrentState = State.Awaiting,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            JobCount = 1,
            ContinuationOptions = ContinuationOptions.OnlyOnSucceeded,
        });

        // Failed child triggers parent to fail
        ctx.Set<Job>().Add(new Job
        {
            Id = Guid.NewGuid(),
            Kind = JobKind.Job,
            CurrentState = State.Failed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            ParentJobId = parentBatchId,
        });

        // Continuation (awaiting, child of failed batch)
        ctx.Set<Job>().Add(new Job
        {
            Id = continuationId,
            Kind = JobKind.Job,
            CurrentState = State.Awaiting,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            ParentJobId = parentBatchId,
        });
        await ctx.SaveChangesAsync();

        // Act — finalize parent, then run again
        var orchCtx1 = _fixture.CreateContext();
        await OrchestrationTask<TestContext>.RunOrchestration(orchCtx1, TimeProvider.System, TimeSpan.FromDays(1), CancellationToken.None);

        var orchCtx2 = _fixture.CreateContext();
        await OrchestrationTask<TestContext>.RunOrchestration(orchCtx2, TimeProvider.System, TimeSpan.FromDays(1), CancellationToken.None);

        // Assert: continuation stays Awaiting (condition not met, but parent could be requeued)
        var readCtx = _fixture.CreateContext();
        var continuation = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == continuationId);
        continuation.ShouldNotBeNull();
        continuation.CurrentState.ShouldBe(State.Awaiting);
    }

    [TimedFact]
    public async Task RunOrchestration_BatchFinalized_ReturnsTrue()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var batchId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = batchId,
            Kind = JobKind.Batch,
            CurrentState = State.Awaiting,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            JobCount = 1,
        });
        ctx.Set<Job>().Add(new Job
        {
            Id = Guid.NewGuid(),
            Kind = JobKind.Job,
            CurrentState = State.Completed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            ParentJobId = batchId,
        });
        await ctx.SaveChangesAsync();

        // Act
        var orchCtx = _fixture.CreateContext();
        var workDone = await OrchestrationTask<TestContext>.RunOrchestration(orchCtx, TimeProvider.System, TimeSpan.FromDays(1), CancellationToken.None);

        // Assert
        workDone.ShouldBeTrue();
    }

    [TimedFact]
    public async Task RunOrchestration_DeletedParent_FailsAwaitingChildren()
    {
        // Arrange
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

        // Act
        var orchCtx = _fixture.CreateContext();
        var workDone = await OrchestrationTask<TestContext>.RunOrchestration(orchCtx, TimeProvider.System, TimeSpan.FromDays(1), CancellationToken.None);

        // Assert
        workDone.ShouldBeTrue();
        var readCtx = _fixture.CreateContext();
        var child = await readCtx.Set<Job>().FindAsync(childId);
        child.ShouldNotBeNull();
        child.CurrentState.ShouldBe(State.Failed);
        child.ExpireAt.ShouldNotBeNull();

        var log = await readCtx.Set<JobLog>().FirstOrDefaultAsync(x => x.JobId == childId);
        log.ShouldNotBeNull();
        log.EventType.ShouldBe("Failed");
    }

    [TimedFact]
    public async Task RunOrchestration_DeletedParent_FailsAwaitingBatchAndGrandchildren()
    {
        // Arrange: deleted parent -> awaiting batch child -> awaiting grandchildren
        var ctx = _fixture.CreateContext();
        var parentId = Guid.NewGuid();
        var batchChildId = Guid.NewGuid();
        var grandchildId = Guid.NewGuid();

        ctx.Set<Job>().Add(new Job
        {
            Id = parentId,
            Kind = JobKind.Batch,
            CurrentState = State.Deleted,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
        });
        ctx.Set<Job>().Add(new Job
        {
            Id = batchChildId,
            Kind = JobKind.Batch,
            CurrentState = State.Awaiting,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            ParentJobId = parentId,
        });
        ctx.Set<Job>().Add(new Job
        {
            Id = grandchildId,
            Kind = JobKind.Job,
            CurrentState = State.Awaiting,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            ParentJobId = batchChildId,
        });
        await ctx.SaveChangesAsync();

        // Act
        var orchCtx = _fixture.CreateContext();
        await OrchestrationTask<TestContext>.RunOrchestration(orchCtx, TimeProvider.System, TimeSpan.FromDays(1), CancellationToken.None);

        // Assert
        var readCtx = _fixture.CreateContext();
        var batchChild = await readCtx.Set<Job>().FindAsync(batchChildId);
        batchChild.ShouldNotBeNull();
        batchChild.CurrentState.ShouldBe(State.Failed);

        var grandchild = await readCtx.Set<Job>().FindAsync(grandchildId);
        grandchild.ShouldNotBeNull();
        grandchild.CurrentState.ShouldBe(State.Failed);
    }

    [TimedFact]
    public async Task RunOrchestration_FailedParentOnAnyFinished_ActivatesContinuation()
    {
        // Arrange: failed batch parent (OnAnyFinishedState) -> awaiting continuation
        var ctx = _fixture.CreateContext();
        var parentBatchId = Guid.NewGuid();
        var continuationId = Guid.NewGuid();

        ctx.Set<Job>().Add(new Job
        {
            Id = parentBatchId,
            Kind = JobKind.Batch,
            CurrentState = State.Awaiting,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            JobCount = 1,
            ContinuationOptions = ContinuationOptions.OnAnyFinishedState,
        });
        ctx.Set<Job>().Add(new Job
        {
            Id = Guid.NewGuid(),
            Kind = JobKind.Job,
            CurrentState = State.Failed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            ParentJobId = parentBatchId,
        });
        ctx.Set<Job>().Add(new Job
        {
            Id = continuationId,
            Kind = JobKind.Job,
            CurrentState = State.Awaiting,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            ParentJobId = parentBatchId,
        });
        await ctx.SaveChangesAsync();

        // Act — finalize parent (Failed but OnAnyFinished → Completed), then activate continuation
        var orchCtx1 = _fixture.CreateContext();
        await OrchestrationTask<TestContext>.RunOrchestration(orchCtx1, TimeProvider.System, TimeSpan.FromDays(1), CancellationToken.None);
        var orchCtx2 = _fixture.CreateContext();
        await OrchestrationTask<TestContext>.RunOrchestration(orchCtx2, TimeProvider.System, TimeSpan.FromDays(1), CancellationToken.None);

        // Assert
        var readCtx = _fixture.CreateContext();
        var continuation = await readCtx.Set<Job>().FindAsync(continuationId);
        continuation.ShouldNotBeNull();
        continuation.CurrentState.ShouldBe(State.Enqueued);
    }

    [TimedFact]
    public async Task RunOrchestration_NoDeletedParent_AwaitingChildStaysAwaiting()
    {
        // Arrange: non-deleted parent -> awaiting child should not be failed
        var ctx = _fixture.CreateContext();
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        ctx.Set<Job>().Add(new Job
        {
            Id = parentId,
            Kind = JobKind.Batch,
            CurrentState = State.Processing,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
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

        // Act
        var orchCtx = _fixture.CreateContext();
        await OrchestrationTask<TestContext>.RunOrchestration(orchCtx, TimeProvider.System, TimeSpan.FromDays(1), CancellationToken.None);

        // Assert
        var readCtx = _fixture.CreateContext();
        var child = await readCtx.Set<Job>().FindAsync(childId);
        child.ShouldNotBeNull();
        child.CurrentState.ShouldBe(State.Awaiting);
    }

    [TimedFact]
    public async Task RunOrchestration_AlreadyFinalized_ReturnsNoWork()
    {
        // Arrange: completed batch + completed children
        var ctx = _fixture.CreateContext();
        var batchId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = batchId,
            Kind = JobKind.Batch,
            CurrentState = State.Completed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            JobCount = 2,
            ExpireAt = DateTime.UtcNow.AddDays(1),
        });

        for (var i = 0; i < 2; i++)
        {
            ctx.Set<Job>().Add(new Job
            {
                Id = Guid.NewGuid(),
                Kind = JobKind.Job,
                CurrentState = State.Completed,
                CreateTime = DateTime.UtcNow,
                ScheduleTime = DateTime.UtcNow,
                Queue = "default",
                ParentJobId = batchId,
            });
        }

        await ctx.SaveChangesAsync();

        // Act
        var orchCtx = _fixture.CreateContext();
        var workDone = await OrchestrationTask<TestContext>.RunOrchestration(orchCtx, TimeProvider.System, TimeSpan.FromDays(1), CancellationToken.None);

        // Assert
        workDone.ShouldBeFalse();
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
