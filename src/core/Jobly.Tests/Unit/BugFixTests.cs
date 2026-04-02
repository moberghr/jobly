using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Models;
using Jobly.Core.Services;
using Jobly.Tests.Fixtures;
using Jobly.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobly.Tests.Unit;

public abstract class BugFixTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected BugFixTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// BUG: Expiration cleanup fails with FK violation when parent job has ExpireAt
    /// but child jobs still reference it via ParentJobId.
    /// </summary>
    [Fact]
    public async Task ExpirationCleanup_ParentWithChildren_DoesNotThrowFkViolation()
    {
        // Arrange: parent with ExpireAt in the past, child referencing it
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
            ExpireAt = DateTime.UtcNow.AddHours(-1), // expired
        });
        ctx.Set<Job>().Add(new Job
        {
            Id = childId,
            Kind = JobKind.Job,
            CurrentState = State.Completed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            ParentJobId = parentId,
            ExpireAt = DateTime.UtcNow.AddHours(-1), // also expired
        });
        await ctx.SaveChangesAsync();

        // Act: should not throw FK violation
        var cleanCtx = _fixture.CreateContext();
        await Should.NotThrowAsync(async () =>
            await ExpirationCleanupTask<TestContext>.RunCleanup(cleanCtx, TimeProvider.System));

        // Assert: both should be deleted
        var readCtx = _fixture.CreateContext();
        var parent = await readCtx.Set<Job>().FindAsync(parentId);
        var child = await readCtx.Set<Job>().FindAsync(childId);
        parent.ShouldBeNull("Parent should be cleaned up");
        child.ShouldBeNull("Child should be cleaned up");
    }

    /// <summary>
    /// BUG: Expiration cleanup fails when parent is expired but child is not yet expired.
    /// Parent can't be deleted because child still references it.
    /// </summary>
    [Fact]
    public async Task ExpirationCleanup_ParentExpiredChildNot_DoesNotThrowFkViolation()
    {
        // Arrange: parent expired, child NOT expired (still has future ExpireAt)
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
            ExpireAt = DateTime.UtcNow.AddHours(-1), // expired
        });
        ctx.Set<Job>().Add(new Job
        {
            Id = childId,
            Kind = JobKind.Job,
            CurrentState = State.Completed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            ParentJobId = parentId,
            ExpireAt = DateTime.UtcNow.AddDays(1), // NOT expired
        });
        await ctx.SaveChangesAsync();

        // Act: should not throw
        var cleanCtx = _fixture.CreateContext();
        await Should.NotThrowAsync(async () =>
            await ExpirationCleanupTask<TestContext>.RunCleanup(cleanCtx, TimeProvider.System));

        // Assert: parent should NOT be deleted (child still references it)
        // OR both deleted together — either way no FK error
        var readCtx = _fixture.CreateContext();
        var child = await readCtx.Set<Job>().FindAsync(childId);
        if (child != null)
        {
            // Child survived — parent must also survive (FK intact)
            var parent = await readCtx.Set<Job>().FindAsync(parentId);
            parent.ShouldNotBeNull("Parent can't be deleted while child exists");
        }
    }

    /// <summary>
    /// BUG: JobModel doesn't include HandlerType. Job lists should show handler info.
    /// </summary>
    [Fact]
    public async Task GetJobsList_IncludesHandlerType()
    {
        // Arrange: create a job with a handler type
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Completed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            Type = "MyApp.Jobs.SendEmail, MyApp",
            HandlerType = "MyApp.Handlers.SendEmailHandler, MyApp",
        });
        await ctx.SaveChangesAsync();

        // Act
        var svc = new JobQueryService<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        var result = await svc.GetJobsList(new BaseListRequest { Page = 0, PageSize = 20 }, State.Completed);

        // Assert
        result.Items.Count.ShouldBe(1);
        result.Items[0].HandlerType.ShouldBe("MyApp.Handlers.SendEmailHandler, MyApp");
    }
}

[Collection("PostgreSql")]
public class BugFixTests_PostgreSql : BugFixTestsBase
{
    public BugFixTests_PostgreSql(PostgreSqlFixture fixture) : base(fixture) { }
}

[Collection("SqlServer")]
[Trait("Category", "SqlServer")]
public class BugFixTests_SqlServer : BugFixTestsBase
{
    public BugFixTests_SqlServer(SqlServerFixture fixture) : base(fixture) { }
}
