using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Notifications;
using Jobly.Tests.Fixtures;
using Jobly.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Scheduling;

[GenerateDatabaseTests(FixtureKind.Default)]
public abstract class ScheduledJobActivationTaskTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected ScheduledJobActivationTaskTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync() => await _fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [TimedFact]
    public async Task Activate_WhenScheduleTimeElapsed_FlipsToEnqueued()
    {
        var ctx = _fixture.CreateContext();
        var now = DateTime.UtcNow;
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Scheduled,
            Queue = "default",
            Type = "TestType",
            Message = "{}",
            CreateTime = now.AddMinutes(-5),
            ScheduleTime = now.AddMinutes(-1),
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        var actCtx = _fixture.CreateContext();
        var activated = await ScheduledJobActivationTask<TestContext>.Activate(actCtx, TimeProvider.System, CancellationToken.None);

        activated.ShouldBe(1);
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FindAsync([jobId], Xunit.TestContext.Current.CancellationToken);
        job.ShouldNotBeNull();
        job.CurrentState.ShouldBe(State.Enqueued);
    }

    [TimedFact]
    public async Task Activate_WhenScheduleTimeInFuture_LeavesAlone()
    {
        var ctx = _fixture.CreateContext();
        var now = DateTime.UtcNow;
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Scheduled,
            Queue = "default",
            Type = "TestType",
            Message = "{}",
            CreateTime = now,
            ScheduleTime = now.AddMinutes(10),
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        var actCtx = _fixture.CreateContext();
        var activated = await ScheduledJobActivationTask<TestContext>.Activate(actCtx, TimeProvider.System, CancellationToken.None);

        activated.ShouldBe(0);
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FindAsync([jobId], Xunit.TestContext.Current.CancellationToken);
        job.ShouldNotBeNull();
        job.CurrentState.ShouldBe(State.Scheduled);
    }

    [TimedFact]
    public async Task Activate_DoesNotTouchEnqueuedRows()
    {
        var ctx = _fixture.CreateContext();
        var now = DateTime.UtcNow;
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Enqueued,
            Queue = "default",
            Type = "TestType",
            Message = "{}",
            CreateTime = now,
            ScheduleTime = now.AddMinutes(-1),
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        var actCtx = _fixture.CreateContext();
        var activated = await ScheduledJobActivationTask<TestContext>.Activate(actCtx, TimeProvider.System, CancellationToken.None);

        activated.ShouldBe(0);
    }

    [TimedFact]
    public async Task Activate_MultipleDueRows_ActivatesAll()
    {
        var ctx = _fixture.CreateContext();
        var now = DateTime.UtcNow;
        for (var i = 0; i < 5; i++)
        {
            ctx.Set<Job>().Add(new Job
            {
                Id = Guid.NewGuid(),
                Kind = JobKind.Job,
                CurrentState = State.Scheduled,
                Queue = i % 2 == 0 ? "default" : "critical",
                Type = "TestType",
                Message = "{}",
                CreateTime = now.AddMinutes(-5),
                ScheduleTime = now.AddMinutes(-1),
            });
        }

        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        var actCtx = _fixture.CreateContext();
        var activated = await ScheduledJobActivationTask<TestContext>.Activate(actCtx, TimeProvider.System, CancellationToken.None);

        activated.ShouldBe(5);
        var readCtx = _fixture.CreateContext();
        var remaining = await readCtx.Set<Job>()
            .CountAsync(x => x.CurrentState == State.Scheduled, Xunit.TestContext.Current.CancellationToken);
        remaining.ShouldBe(0);
    }

    [TimedFact]
    public async Task ActivateWithNotify_FiresOneJobEnqueuedPerDistinctQueue()
    {
        var ctx = _fixture.CreateContext();
        var now = DateTime.UtcNow;
        foreach (var queue in new[] { "default", "default", "critical" })
        {
            ctx.Set<Job>().Add(new Job
            {
                Id = Guid.NewGuid(),
                Kind = JobKind.Job,
                CurrentState = State.Scheduled,
                Queue = queue,
                Type = "TestType",
                Message = "{}",
                CreateTime = now.AddMinutes(-5),
                ScheduleTime = now.AddMinutes(-1),
            });
        }

        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        var transport = new RecordingTransport();
        var actCtx = _fixture.CreateContext();
        var result = await ScheduledJobActivationTask<TestContext>.ActivateWithNotify(actCtx, TimeProvider.System, transport, CancellationToken.None);

        result.Activated.ShouldBe(3);
        transport.Published.Count.ShouldBe(2);
        transport.Published.ShouldContain(n => n.Kind == NotificationKind.JobEnqueued && n.Queue == "default");
        transport.Published.ShouldContain(n => n.Kind == NotificationKind.JobEnqueued && n.Queue == "critical");
    }

    private sealed class RecordingTransport : IJoblyNotificationTransport
    {
        public List<Notification> Published { get; } = [];

        public Task PublishAsync(NotificationKind kind, string? queue, CancellationToken ct)
        {
            Published.Add(new Notification(kind, queue));
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<Notification> ListenAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            yield break;
        }
    }
}
