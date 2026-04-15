using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Tests.Fixtures;
using Jobly.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Unit;

public abstract class OrchestrationTaskTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected OrchestrationTaskTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync() => await _fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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
}

[Collection<PostgreSqlCollection>]
public class OrchestrationTaskTests_PostgreSql : OrchestrationTaskTestsBase
{
    public OrchestrationTaskTests_PostgreSql(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }
}

[Collection<SqlServerCollection>]
[Trait("Category", "SqlServer")]
public class OrchestrationTaskTests_SqlServer : OrchestrationTaskTestsBase
{
    public OrchestrationTaskTests_SqlServer(SqlServerFixture fixture)
        : base(fixture)
    {
    }
}
