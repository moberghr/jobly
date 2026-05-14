using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Warp.Core;
using Warp.Core.Data.Entities;
using Warp.Core.Events;
using Warp.Tests.Fixtures;
using Warp.Worker.Services;

namespace Warp.Tests.Worker;

// Bookkeeping-overhead regressions:
//   1. GetIntervalAsync caches the interval_seconds DB read for ~1 minute so each task
//      iteration doesn't issue a fresh SELECT when nothing has changed.
//   2. TryUpdateServerTaskAsync coalesces consecutive "Skipped" updates so an idle task
//      (e.g. MessageRouter polling for messages that never arrive) doesn't UPDATE the
//      server_task row every loop. State transitions (Completed/Failed) always flush.
//
// Both behaviors are observable via the cached fields exposed for test inspection. Real-
// world impact (DB query count under load) is covered indirectly by integration suites.
[GenerateDatabaseTests]
public abstract class ServerTaskLoopBookkeepingTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected ServerTaskLoopBookkeepingTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync() => await _fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [TimedFact]
    public async Task GetIntervalAsync_WithinCacheWindow_ReturnsCachedValueWhenDbChanges()
    {
        var serverTaskId = await SeedServerTaskAsync(intervalSeconds: 5.0);
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var loop = BuildLoop(time);
        loop.SetServerTaskIdForTest(serverTaskId);

        var first = await loop.GetIntervalAsync(CancellationToken.None);
        first.ShouldBe(TimeSpan.FromSeconds(5));

        // Mutate the underlying row OUT-OF-BAND: a different process editing IntervalSeconds.
        // Within the cache window the loop should still return its cached value, not re-read.
        await using (var ctx = _fixture.CreateContext())
        {
            await ctx.Set<ServerTask>()
                .Where(x => x.Id == serverTaskId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IntervalSeconds, 999.0));
        }

        // 30s elapsed — still inside the 1m cache window.
        time.Advance(TimeSpan.FromSeconds(30));
        var second = await loop.GetIntervalAsync(CancellationToken.None);
        second.ShouldBe(TimeSpan.FromSeconds(5));
    }

    [TimedFact]
    public async Task GetIntervalAsync_AfterCacheExpires_RereadsFromDb()
    {
        var serverTaskId = await SeedServerTaskAsync(intervalSeconds: 5.0);
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var loop = BuildLoop(time);
        loop.SetServerTaskIdForTest(serverTaskId);

        await loop.GetIntervalAsync(CancellationToken.None);

        await using (var ctx = _fixture.CreateContext())
        {
            await ctx.Set<ServerTask>()
                .Where(x => x.Id == serverTaskId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IntervalSeconds, 999.0));
        }

        // Push past the 1m cache lifetime — next call should hit the DB.
        time.Advance(TimeSpan.FromMinutes(2));
        var refreshed = await loop.GetIntervalAsync(CancellationToken.None);

        refreshed.ShouldBe(TimeSpan.FromSeconds(999));
    }

    [TimedFact]
    public async Task TryUpdateServerTaskAsync_ConsecutiveSkipped_CoalescesToOneUpdate()
    {
        var serverTaskId = await SeedServerTaskAsync(intervalSeconds: 1.0);
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var loop = BuildLoop(time);
        loop.SetServerTaskIdForTest(serverTaskId);

        // First Skipped — must flush so the dashboard sees the task is alive at all.
        await loop.TryUpdateServerTaskAsync("Skipped", null, 100);
        var afterFirst = await ReadAsync(serverTaskId);
        afterFirst.LastStatus.ShouldBe("Skipped");
        afterFirst.LastDurationMs.ShouldBe(100);

        // Out-of-band poison: if the next call DOES UPDATE, LastDurationMs gets overwritten.
        await using (var ctx = _fixture.CreateContext())
        {
            await ctx.Set<ServerTask>()
                .Where(x => x.Id == serverTaskId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastDurationMs, -1.0));
        }

        // Second Skipped within the 30s throttle window — should be a no-op write.
        time.Advance(TimeSpan.FromSeconds(5));
        await loop.TryUpdateServerTaskAsync("Skipped", null, 200);
        var afterSecond = await ReadAsync(serverTaskId);
        afterSecond.LastDurationMs.ShouldBe(-1.0, "second Skipped should not have UPDATEd");
    }

    [TimedFact]
    public async Task TryUpdateServerTaskAsync_StatusTransitionToSkipped_AlwaysFlushes()
    {
        var serverTaskId = await SeedServerTaskAsync(intervalSeconds: 1.0);
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var loop = BuildLoop(time);
        loop.SetServerTaskIdForTest(serverTaskId);

        // Completed → Skipped is a state transition and must flush even though both calls
        // are close together. The throttle only coalesces *consecutive* Skipped pairs.
        await loop.TryUpdateServerTaskAsync("Completed", "did work", 50);
        time.Advance(TimeSpan.FromSeconds(1));
        await loop.TryUpdateServerTaskAsync("Skipped", null, 100);

        var row = await ReadAsync(serverTaskId);
        row.LastStatus.ShouldBe("Skipped");
        row.LastDurationMs.ShouldBe(100);
    }

    [TimedFact]
    public async Task TryUpdateServerTaskAsync_SkippedAfterThrottleWindow_Flushes()
    {
        var serverTaskId = await SeedServerTaskAsync(intervalSeconds: 1.0);
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var loop = BuildLoop(time);
        loop.SetServerTaskIdForTest(serverTaskId);

        await loop.TryUpdateServerTaskAsync("Skipped", null, 100);

        // Advance past the 5min throttle window.
        time.Advance(TimeSpan.FromMinutes(6));
        await loop.TryUpdateServerTaskAsync("Skipped", null, 200);

        var row = await ReadAsync(serverTaskId);
        row.LastDurationMs.ShouldBe(200, "Skipped past the throttle window must flush");
    }

    private async Task<int> SeedServerTaskAsync(double intervalSeconds)
    {
        await using var ctx = _fixture.CreateContext();
        var serverId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        ctx.Set<Server>().Add(new Server
        {
            Id = serverId,
            StartedTime = now,
            LastHeartbeatTime = now,
        });
        var entity = new ServerTask
        {
            ServerId = serverId,
            TaskName = $"test-task-{Guid.NewGuid()}",
            IntervalSeconds = intervalSeconds,
        };
        ctx.Set<ServerTask>().Add(entity);
        await ctx.SaveChangesAsync();

        return entity.Id;
    }

    private async Task<ServerTask> ReadAsync(int id)
    {
        await using var ctx = _fixture.CreateContext();

        return await ctx.Set<ServerTask>().AsNoTracking().FirstAsync(x => x.Id == id);
    }

    private ServerTaskLoop<TestContext> BuildLoop(TimeProvider time)
    {
        return new ServerTaskLoop<TestContext>(
            new StubTask(),
            new FixtureScopeFactory(_fixture),
            new NoopLockProvider(),
            time,
            Guid.NewGuid(),
            NullLogger.Instance);
    }

    private sealed class StubTask : IServerTask
    {
        public string Name => "Stub";

        public string? LockKey => null;

        public TimeSpan? DefaultInterval => TimeSpan.FromSeconds(1);

        public Task<string?> ExecuteAsync(CancellationToken ct) => Task.FromResult<string?>(null);
    }

    private sealed class NoopLockProvider : IWarpLockProvider
    {
        public Task<IAsyncDisposable?> TryAcquireAsync(string name, TimeSpan timeout, CancellationToken ct) =>
            Task.FromResult<IAsyncDisposable?>(new NoopHandle());

        private sealed class NoopHandle : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class FixtureScopeFactory : IServiceScopeFactory
    {
        private readonly IDatabaseFixture _fixture;

        public FixtureScopeFactory(IDatabaseFixture fixture) => _fixture = fixture;

        public IServiceScope CreateScope() => new FixtureScope(_fixture);

        private sealed class FixtureScope : IServiceScope
        {
            private readonly TestContext _context;

            public FixtureScope(IDatabaseFixture fixture)
            {
                _context = fixture.CreateContext();
                ServiceProvider = new ContextProvider(_context);
            }

            public IServiceProvider ServiceProvider { get; }

            public void Dispose() => _context.Dispose();
        }

        private sealed class ContextProvider : IServiceProvider
        {
            private readonly TestContext _context;

            public ContextProvider(TestContext context) => _context = context;

            public object? GetService(Type serviceType) =>
                serviceType == typeof(TestContext) ? _context : null;
        }
    }

    // Minimal monotonic time provider for tests — we don't need scheduler integration
    // (no Task.Delay anywhere in the throttle / cache paths), just GetUtcNow control.
    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public FakeTimeProvider(DateTimeOffset start) => _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }
}
