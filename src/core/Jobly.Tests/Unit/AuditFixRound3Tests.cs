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

public abstract class AuditFixRound3TestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected AuditFixRound3TestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync() => await _fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>
    /// Count-based cleanup must not delete parents whose children haven't expired.
    /// </summary>
    [TimedFact]
    public async Task CountBasedCleanup_SkipsParentsWithNonExpiredChildren()
    {
        // Arrange: 5 standalone expired jobs + 1 parent with non-expired child
        var ctx = _fixture.CreateContext();

        for (var i = 0; i < 5; i++)
        {
            ctx.Set<Job>().Add(new Job
            {
                Kind = JobKind.Job,
                CurrentState = State.Completed,
                CreateTime = DateTime.UtcNow,
                ScheduleTime = DateTime.UtcNow,
                Queue = "default",
                ExpireAt = DateTime.UtcNow.AddHours(-i - 1),
            });
        }

        var parentId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = parentId,
            Kind = JobKind.Batch,
            CurrentState = State.Completed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            ExpireAt = DateTime.UtcNow.AddHours(-1), // expired
        });
        ctx.Set<Job>().Add(new Job
        {
            Kind = JobKind.Job,
            CurrentState = State.Completed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            ParentJobId = parentId,
            ExpireAt = DateTime.UtcNow.AddDays(1), // NOT expired
        });

        await ctx.SaveChangesAsync();

        // Act: run count-based cleanup with threshold of 3
        var cleanCtx = _fixture.CreateContext();
        await Should.NotThrowAsync(async () =>
            await ExpirationCleanupTask<TestContext>.RunCountBasedCleanup(cleanCtx, 3, 1000));

        // Assert: parent should survive (child not expired), standalone jobs reduced to 3
        var readCtx = _fixture.CreateContext();
        var parent = await readCtx.Set<Job>().FindAsync(parentId);
        parent.ShouldNotBeNull("Parent with non-expired child should not be deleted");

        var standaloneCount = await readCtx.Set<Job>()
            .Where(j => j.ParentJobId == null && j.Kind == JobKind.Job)
            .CountAsync();
        standaloneCount.ShouldBeLessThanOrEqualTo(3);
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
        await ctx.SaveChangesAsync();

        // Act: requeue child1
        var svc = new JobCommandService<TestContext>(_fixture.CreateContext(), TimeProvider.System, Options.Create(new JoblyConfiguration()));
        await svc.RequeueJob(child1Id);

        // Assert: parent should be back in Awaiting, JobCount incremented, ExpireAt cleared
        var readCtx = _fixture.CreateContext();
        var parent = await readCtx.Set<Job>().FindAsync(parentId);
        parent.ShouldNotBeNull();
        parent.CurrentState.ShouldBe(State.Awaiting);
        parent.ExpireAt.ShouldBeNull();
        parent.JobCount.ShouldBe(3);

        var child1 = await readCtx.Set<Job>().FindAsync(child1Id);
        child1.ShouldNotBeNull();
        child1.CurrentState.ShouldBe(State.Enqueued);
        child1.ScheduleTime.ShouldBeLessThanOrEqualTo(DateTime.UtcNow.AddSeconds(5));
    }
}

[Collection<PostgreSqlCollection>]
public class AuditFixRound3Tests_PostgreSql : AuditFixRound3TestsBase
{
    public AuditFixRound3Tests_PostgreSql(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }
}

[Collection<SqlServerCollection>]
[Trait("Category", "SqlServer")]
public class AuditFixRound3Tests_SqlServer : AuditFixRound3TestsBase
{
    public AuditFixRound3Tests_SqlServer(SqlServerFixture fixture)
        : base(fixture)
    {
    }
}
