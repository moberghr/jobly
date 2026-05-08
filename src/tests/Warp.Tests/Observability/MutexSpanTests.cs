using System.Diagnostics;
using Shouldly;
using Warp.Core;
using Warp.Core.Enums;
using Warp.Core.Handlers;
using Warp.Core.Logging;
using Warp.Core.Mutex;
using Warp.Tests.Helpers;

namespace Warp.Tests.Observability;

/// <summary>
/// Pure unit tests for the warp.mutex_acquire span emitted by MutexPipelineBehavior.
/// Uses an in-memory IWarpLockProvider double — no database required.
/// </summary>
[Trait("Category", "NoDb")]
[Collection("Telemetry")]
public class MutexSpanTests
{
    private sealed class FakeLockProvider : IWarpLockProvider
    {
        public bool ShouldAcquire { get; set; } = true;

        public Task<IAsyncDisposable?> TryAcquireAsync(string name, TimeSpan timeout, CancellationToken ct)
            => Task.FromResult<IAsyncDisposable?>(ShouldAcquire ? new FakeHandle() : null);

        private sealed class FakeHandle : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class TestJob : IJob;

    private sealed class TestJobNoMutex : IJob;

    [TimedFact]
    public async Task HandleAsync_AcquiredPath_EmitsMutexAcquireSpanWithAcquiredTrue()
    {
        using var harness = new ActivityListenerHarness();
        var jobContext = new JobContext { JobId = Guid.NewGuid(), TraceId = Guid.NewGuid() };
        jobContext.Metadata["ConcurrencyKey"] = "user-42";
        var lockProvider = new FakeLockProvider { ShouldAcquire = true };
        var behavior = new MutexPipelineBehavior<TestJob, Unit>(jobContext, lockProvider, TimeProvider.System);

        await behavior.HandleAsync(new TestJob(), (req, ct) => Task.FromResult(Unit.Value), CancellationToken.None);

        var span = harness.FirstByName("warp.mutex_acquire");
        span.ShouldNotBeNull();
        span.Kind.ShouldBe(ActivityKind.Internal);
        span.GetTagItem(WarpTelemetryAttributes.WarpMutexKey).ShouldBe("user-42");
        span.GetTagItem(WarpTelemetryAttributes.WarpMutexAcquired).ShouldBe(true);
    }

    [TimedFact]
    public async Task HandleAsync_HeldByOther_EmitsMutexAcquireSpanWithAcquiredFalseAndShortCircuits()
    {
        using var harness = new ActivityListenerHarness();
        var jobContext = new JobContext { JobId = Guid.NewGuid(), TraceId = Guid.NewGuid() };
        jobContext.Metadata["ConcurrencyKey"] = "user-99";
        var lockProvider = new FakeLockProvider { ShouldAcquire = false };
        var behavior = new MutexPipelineBehavior<TestJob, Unit>(jobContext, lockProvider, TimeProvider.System);
        var nextCalled = false;

        await behavior.HandleAsync(
            new TestJob(),
            (req, ct) =>
            {
                nextCalled = true;

                return Task.FromResult(Unit.Value);
            },
            CancellationToken.None);

        nextCalled.ShouldBeFalse();
        jobContext.Outcome.ShouldNotBeNull();
        jobContext.Outcome.State.ShouldBe(State.Deleted);

        var span = harness.FirstByName("warp.mutex_acquire");
        span.ShouldNotBeNull();
        span.GetTagItem(WarpTelemetryAttributes.WarpMutexAcquired).ShouldBe(false);
    }

    [TimedFact]
    public async Task HandleAsync_NoConcurrencyKey_NoMutexSpanEmitted()
    {
        using var harness = new ActivityListenerHarness();
        var jobContext = new JobContext { JobId = Guid.NewGuid(), TraceId = Guid.NewGuid() };
        var lockProvider = new FakeLockProvider();
        var behavior = new MutexPipelineBehavior<TestJobNoMutex, Unit>(jobContext, lockProvider, TimeProvider.System);

        await behavior.HandleAsync(new TestJobNoMutex(), (req, ct) => Task.FromResult(Unit.Value), CancellationToken.None);

        harness.FirstByName("warp.mutex_acquire").ShouldBeNull();
    }

    private sealed class NotAJobRequest : IRequest<Unit>;

    private sealed class ThrowingLockProvider : IWarpLockProvider
    {
        public Task<IAsyncDisposable?> TryAcquireAsync(string name, TimeSpan timeout, CancellationToken ct)
            => throw new InvalidOperationException("simulated lock-provider failure");
    }

    [TimedFact]
    public async Task HandleAsync_LockProviderThrows_MutexSpanIsStillCapturedAndExceptionPropagates()
    {
        using var harness = new ActivityListenerHarness();
        var jobContext = new JobContext { JobId = Guid.NewGuid(), TraceId = Guid.NewGuid() };
        jobContext.Metadata["ConcurrencyKey"] = "user-99";
        var lockProvider = new ThrowingLockProvider();
        var behavior = new MutexPipelineBehavior<TestJob, Unit>(jobContext, lockProvider, TimeProvider.System);

        await Should.ThrowAsync<InvalidOperationException>(() =>
            behavior.HandleAsync(new TestJob(), (req, ct) => Task.FromResult(Unit.Value), CancellationToken.None));

        // The `using var mutexSpan = WarpTelemetry.StartMutexActivity()` block must dispose the
        // activity even when TryAcquireAsync throws. Without that lifetime guarantee we'd leak
        // open spans on lock-provider failures (network blips, etc.).
        var span = harness.FirstByName("warp.mutex_acquire");
        span.ShouldNotBeNull();
        span.GetTagItem(WarpTelemetryAttributes.WarpMutexKey).ShouldBe("user-99");

        // warp.mutex.acquired never gets set because TryAcquireAsync threw before returning.
        span.GetTagItem(WarpTelemetryAttributes.WarpMutexAcquired).ShouldBeNull();
    }

    [TimedFact]
    public async Task HandleAsync_RequestNotIJob_NoMutexSpanEmittedAndNextCalled()
    {
        using var harness = new ActivityListenerHarness();
        var jobContext = new JobContext { JobId = Guid.NewGuid(), TraceId = Guid.NewGuid() };
        jobContext.Metadata["ConcurrencyKey"] = "should-be-ignored";
        var lockProvider = new FakeLockProvider();
        var behavior = new MutexPipelineBehavior<NotAJobRequest, Unit>(jobContext, lockProvider, TimeProvider.System);
        var nextCalled = false;

        await behavior.HandleAsync(
            new NotAJobRequest(),
            (req, ct) =>
            {
                nextCalled = true;

                return Task.FromResult(Unit.Value);
            },
            CancellationToken.None);

        nextCalled.ShouldBeTrue();
        harness.FirstByName("warp.mutex_acquire").ShouldBeNull();
    }
}
