using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Warp.Core.Enums;
using Warp.Core.Handlers;
using Warp.Core.Timeout;
using Warp.Tests.TestData.Handlers;

namespace Warp.Tests.Features.Timeout;

[Trait("Category", "NoDb")]
public class TimeoutPipelineBehaviorTests
{
    private static TimeoutPipelineBehavior<UnitRequest, Unit> Build(FakeTimeProvider time, JobContext context)
    {
        return new TimeoutPipelineBehavior<UnitRequest, Unit>(context, time);
    }

    [TimedFact]
    public async Task NonJobRequest_PassesThroughWithoutTimeout()
    {
        // request is not IJob → bail immediately. Timeout addon is job-only; in-memory
        // IRequest<T> callers wrap their own CancellationToken if they need a deadline.
        // Even with TimeoutSeconds metadata set, the behavior must be a no-op.
        var time = new FakeTimeProvider();
        var ctx = new JobContext { JobId = Guid.NewGuid() };
        ctx.Metadata["TimeoutSeconds"] = 1L;
        ctx.Metadata["TimeoutMode"] = (int)TimeoutMode.Delete;

        var behavior = new TimeoutPipelineBehavior<GetGreetingRequest, string>(ctx, time);

        var result = await behavior.HandleAsync(
            new GetGreetingRequest { Name = "test" },
            (req, ct) => Task.FromResult("ok"),
            CancellationToken.None);

        result.ShouldBe("ok");
        ctx.Outcome.ShouldBeNull();
    }

    [TimedFact]
    public async Task NoTimeoutMetadata_PassesThrough()
    {
        var time = new FakeTimeProvider();
        var ctx = new JobContext { JobId = Guid.NewGuid() };
        var behavior = Build(time, ctx);

        var result = await behavior.HandleAsync(
            new UnitRequest(),
            (req, ct) => Task.FromResult(Unit.Value),
            CancellationToken.None);

        result.ShouldBe(Unit.Value);
        ctx.Outcome.ShouldBeNull();
    }

    [TimedFact]
    public async Task DeleteMode_HandlerHonoursToken_SetsDeletedOutcome()
    {
        var time = new FakeTimeProvider();
        var ctx = new JobContext { JobId = Guid.NewGuid() };
        ctx.Metadata["TimeoutSeconds"] = 1L;
        ctx.Metadata["TimeoutMode"] = (int)TimeoutMode.Delete;
        var behavior = Build(time, ctx);

        var handlerStarted = new TaskCompletionSource();
        var handlerTask = behavior.HandleAsync(
            new UnitRequest(),
            async (req, ct) =>
            {
                handlerStarted.SetResult();
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct);
                return Unit.Value;
            },
            CancellationToken.None);

        await handlerStarted.Task;
        time.Advance(TimeSpan.FromSeconds(2));

        var result = await handlerTask;

        result.ShouldBe(default(Unit));
        ctx.Outcome.ShouldNotBeNull();
        ctx.Outcome!.State.ShouldBe(State.Deleted);
        ctx.Outcome.LogMessage.ShouldBe("Timed out after 1s");
    }

    [TimedFact]
    public async Task FailMode_HandlerHonoursToken_ThrowsTimeoutException()
    {
        var time = new FakeTimeProvider();
        var ctx = new JobContext { JobId = Guid.NewGuid() };
        ctx.Metadata["TimeoutSeconds"] = 2L;
        ctx.Metadata["TimeoutMode"] = (int)TimeoutMode.Fail;
        var behavior = Build(time, ctx);

        var handlerStarted = new TaskCompletionSource();
        var handlerTask = behavior.HandleAsync(
            new UnitRequest(),
            async (req, ct) =>
            {
                handlerStarted.SetResult();
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct);
                return Unit.Value;
            },
            CancellationToken.None);

        await handlerStarted.Task;
        time.Advance(TimeSpan.FromSeconds(3));

        var ex = await Should.ThrowAsync<TimeoutException>(handlerTask);
        ex.Message.ShouldContain("2s");
        ctx.Outcome.ShouldBeNull();
    }

    [TimedFact]
    public async Task HandlerCompletesBeforeTimeout_NoOutcome()
    {
        var time = new FakeTimeProvider();
        var ctx = new JobContext { JobId = Guid.NewGuid() };
        ctx.Metadata["TimeoutSeconds"] = 60L;
        ctx.Metadata["TimeoutMode"] = (int)TimeoutMode.Delete;
        var behavior = Build(time, ctx);

        var result = await behavior.HandleAsync(
            new UnitRequest(),
            (req, ct) => Task.FromResult(Unit.Value),
            CancellationToken.None);

        result.ShouldBe(Unit.Value);
        ctx.Outcome.ShouldBeNull();
    }

    [TimedFact]
    public async Task TotalScope_DeadlinePast_FiresImmediately()
    {
        var publishMoment = new DateTime(2026, 5, 12, 12, 0, 0, DateTimeKind.Utc);
        var time = new FakeTimeProvider(new DateTimeOffset(publishMoment.AddSeconds(120), TimeSpan.Zero));
        var ctx = new JobContext { JobId = Guid.NewGuid() };
        ctx.Metadata["TimeoutSeconds"] = 30L;
        ctx.Metadata["TimeoutMode"] = (int)TimeoutMode.Fail;
        ctx.Metadata["TimeoutScope"] = (int)TimeoutScope.Total;
        ctx.Metadata["TimeoutDeadlineUtc"] = publishMoment.AddSeconds(30);
        var behavior = Build(time, ctx);

        var handlerStarted = new TaskCompletionSource();
        var handlerTask = behavior.HandleAsync(
            new UnitRequest(),
            async (req, ct) =>
            {
                handlerStarted.SetResult();
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct);
                return Unit.Value;
            },
            CancellationToken.None);

        await handlerStarted.Task;

        // Zero-delay CTS fires synchronously on construction; no Advance needed.
        await Should.ThrowAsync<TimeoutException>(handlerTask);
    }

    [TimedFact]
    public async Task TotalScope_RemainingTimeUsed()
    {
        var publishMoment = new DateTime(2026, 5, 12, 12, 0, 0, DateTimeKind.Utc);

        // 5s have passed since publish; deadline = publish + 10s, so 5s remaining.
        var time = new FakeTimeProvider(new DateTimeOffset(publishMoment.AddSeconds(5), TimeSpan.Zero));
        var ctx = new JobContext { JobId = Guid.NewGuid() };
        ctx.Metadata["TimeoutSeconds"] = 10L;
        ctx.Metadata["TimeoutMode"] = (int)TimeoutMode.Fail;
        ctx.Metadata["TimeoutScope"] = (int)TimeoutScope.Total;
        ctx.Metadata["TimeoutDeadlineUtc"] = publishMoment.AddSeconds(10);
        var behavior = Build(time, ctx);

        var handlerStarted = new TaskCompletionSource();
        var handlerTask = behavior.HandleAsync(
            new UnitRequest(),
            async (req, ct) =>
            {
                handlerStarted.SetResult();
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct);
                return Unit.Value;
            },
            CancellationToken.None);

        await handlerStarted.Task;

        time.Advance(TimeSpan.FromSeconds(4));

        handlerTask.IsCompleted.ShouldBeFalse();

        time.Advance(TimeSpan.FromSeconds(2));

        await Should.ThrowAsync<TimeoutException>(handlerTask);
    }

    [TimedFact]
    public async Task WorkerShutdownDuringHandler_PropagatesOCE_NoTimeoutOutcome()
    {
        var time = new FakeTimeProvider();
        var ctx = new JobContext { JobId = Guid.NewGuid() };
        ctx.Metadata["TimeoutSeconds"] = 60L;
        ctx.Metadata["TimeoutMode"] = (int)TimeoutMode.Delete;
        var behavior = Build(time, ctx);

        using var workerCts = new CancellationTokenSource();
        var handlerStarted = new TaskCompletionSource();
        var handlerTask = behavior.HandleAsync(
            new UnitRequest(),
            async (req, ct) =>
            {
                handlerStarted.SetResult();
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct);
                return Unit.Value;
            },
            workerCts.Token);

        await handlerStarted.Task;
        await workerCts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(handlerTask);
        ctx.Outcome.ShouldBeNull();
    }

    [TimedFact]
    public async Task TotalScope_WithoutDeadlineMetadata_FallsBackToPerAttemptBudget()
    {
        // Defensive: if a job is published with Scope = Total but no DeadlineUtc (only reachable
        // via raw Configure<ITimeoutMetadata> bypassing the WithTimeout extension AND the
        // publish behavior), the pipeline must fall through to the seconds budget rather than
        // crash on a null deadline. Same behaviour as PerAttempt: each attempt gets a fresh
        // `TimeoutSeconds`-long timer.
        var time = new FakeTimeProvider();
        var ctx = new JobContext { JobId = Guid.NewGuid() };
        ctx.Metadata["TimeoutSeconds"] = 1L;
        ctx.Metadata["TimeoutMode"] = (int)TimeoutMode.Delete;
        ctx.Metadata["TimeoutScope"] = (int)TimeoutScope.Total;

        // intentionally omit TimeoutDeadlineUtc
        var behavior = Build(time, ctx);

        var handlerStarted = new TaskCompletionSource();
        var handlerTask = behavior.HandleAsync(
            new UnitRequest(),
            async (req, ct) =>
            {
                handlerStarted.SetResult();
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct);
                return Unit.Value;
            },
            CancellationToken.None);

        await handlerStarted.Task;
        time.Advance(TimeSpan.FromSeconds(2));

        await handlerTask;

        ctx.Outcome.ShouldNotBeNull();
        ctx.Outcome!.State.ShouldBe(State.Deleted);
        ctx.Outcome.LogMessage.ShouldBe("Timed out (deadline exceeded, 1s total budget)");
    }

    [TimedFact]
    public async Task WorkerShutdownConcurrentWithTimerFire_PropagatesOCE_NoTimeoutOutcome()
    {
        // The catch filter guards against the race where the worker is shutting down at the
        // exact moment a timer fires:
        //   when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        // If both are cancelled, the filter evaluates false and the OCE propagates — worker
        // shutdown wins, no spurious Deleted outcome. Job stays Processing so StaleJobRecovery
        // can re-enqueue it after restart.
        var time = new FakeTimeProvider();
        var ctx = new JobContext { JobId = Guid.NewGuid() };
        ctx.Metadata["TimeoutSeconds"] = 1L;
        ctx.Metadata["TimeoutMode"] = (int)TimeoutMode.Delete;
        var behavior = Build(time, ctx);

        using var workerCts = new CancellationTokenSource();
        var handlerStarted = new TaskCompletionSource();
        var handlerTask = behavior.HandleAsync(
            new UnitRequest(),
            async (req, ct) =>
            {
                handlerStarted.SetResult();
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct);
                return Unit.Value;
            },
            workerCts.Token);

        await handlerStarted.Task;

        // Fire BOTH triggers: the inner timer (via fake-time advance) AND the outer worker
        // cancellation. The filter sees cancellationToken.IsCancellationRequested == true, so
        // its guard fails — OCE propagates up. No timeout outcome is set.
        time.Advance(TimeSpan.FromSeconds(2));
        await workerCts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(handlerTask);
        ctx.Outcome.ShouldBeNull();
    }
}
