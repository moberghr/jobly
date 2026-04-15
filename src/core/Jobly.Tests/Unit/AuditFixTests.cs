using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Jobly.Core.Interceptors;
using Jobly.Core.Services;
using Jobly.Tests.Fixtures;
using Jobly.Tests.TestData.Handlers;
using Jobly.Worker;
using Jobly.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobly.Tests.Unit;

/// <summary>
/// Tests for critical audit findings. Each test should FAIL before the fix and PASS after.
/// </summary>
public abstract class AuditFixTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected AuditFixTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync() => await _fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>
    /// CRITICAL #1: Stale recovery must respect CancellationMode.
    /// If a job has CancellationMode=Graceful (user called DeleteJob), stale recovery
    /// should set it to Deleted, NOT requeue it.
    /// </summary>
    [TimedFact]
    public async Task StaleRecovery_WithCancellationModeGraceful_SetsDeletedNotEnqueued()
    {
        // Arrange: a processing job with CancellationMode=Graceful and stale keep-alive
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Processing,
            CancellationMode = CancellationMode.Graceful,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            LastKeepAlive = DateTime.UtcNow.AddMinutes(-10), // stale
        });
        await ctx.SaveChangesAsync();

        // Act: run stale recovery
        var recoveryCtx = _fixture.CreateContext();
        await StaleJobRecoveryTask<TestContext>.RequeueStaleJobs(recoveryCtx, TimeProvider.System, TimeSpan.FromMinutes(5));

        // Assert: job should be Deleted (not Enqueued) because user intended to cancel it
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FindAsync(jobId);
        job.ShouldNotBeNull();
        job.CurrentState.ShouldBe(State.Deleted, "Stale job with CancellationMode=Graceful should be Deleted, not requeued");
        job.CancellationMode.ShouldBe(CancellationMode.None);
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
        await ctx.SaveChangesAsync();

        // Act: requeue the failed child
        var svc = new JobCommandService<TestContext>(_fixture.CreateContext(), TimeProvider.System, Options.Create(new JoblyConfiguration()));
        await svc.RequeueJob(childId);

        // Assert: parent should be back in Awaiting (for batch) and ExpireAt cleared
        var readCtx = _fixture.CreateContext();
        var parent = await readCtx.Set<Job>().FindAsync(parentId);
        parent.ShouldNotBeNull();
        parent.CurrentState.ShouldBe(State.Awaiting);
        parent.ExpireAt.ShouldBeNull();
        parent.JobCount.ShouldBe(2); // incremented from 1

        var child = await readCtx.Set<Job>().FindAsync(childId);
        child.ShouldNotBeNull();
        child.CurrentState.ShouldBe(State.Enqueued);
    }

    /// <summary>
    /// HIGH: No JobLog when message routing finds 0 handlers.
    /// Messages marked Failed should have a log explaining why.
    /// </summary>
    [TimedFact]
    public async Task MessageRouting_NoHandlers_CreatesFailedLogEntry()
    {
        // Arrange: a message with a type that has no registered handlers
        var ctx = _fixture.CreateContext();
        var messageId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = messageId,
            Kind = JobKind.Message,
            CurrentState = State.Enqueued,
            Type = "NonExistent.Type, NonExistent.Assembly",
            Message = "{}",
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
        });
        await ctx.SaveChangesAsync();

        // Act: run message routing
        var routeCtx = _fixture.CreateContext();
        var scopeFactory = CreateScopeFactory();
        await MessageRoutingTask<TestContext>.RunMessageRouting(routeCtx, scopeFactory, TimeProvider.System, CancellationToken.None);

        // Assert: message should be Failed with a log entry explaining why
        var readCtx = _fixture.CreateContext();
        var message = await readCtx.Set<Job>().FindAsync(messageId);
        message.ShouldNotBeNull();
        message.CurrentState.ShouldBe(State.Failed);

        var logs = await readCtx.Set<JobLog>().Where(l => l.JobId == messageId).ToListAsync();
        logs.ShouldNotBeEmpty("Failed message should have a log entry explaining the failure");
        logs.ShouldContain(l => l.EventType == "Failed");
    }

    private IServiceScopeFactory CreateScopeFactory()
    {
        var services = new ServiceCollection();
        services.AddHandlers(typeof(AuditFixTestsBase).Assembly);
        services.AddLogging();
        services.AddScoped<TestContext>(_ => _fixture.CreateContext());

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IServiceScopeFactory>();
    }
}

[Collection<PostgreSqlCollection>]
public class AuditFixTests_PostgreSql : AuditFixTestsBase
{
    public AuditFixTests_PostgreSql(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }
}

[Collection<SqlServerCollection>]
[Trait("Category", "SqlServer")]
public class AuditFixTests_SqlServer : AuditFixTestsBase
{
    public AuditFixTests_SqlServer(SqlServerFixture fixture)
        : base(fixture)
    {
    }
}
