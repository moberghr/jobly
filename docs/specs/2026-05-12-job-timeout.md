# Spec: Job Timeout — opt-in addon

## Problem

Some jobs hang or run far longer than expected — a slow upstream API, a runaway query, a bug. Today, the only ways to stop them are:

- `IJobCommandService.DeleteJob` from an operator (manual)
- Worker restart (kills *all* in-flight jobs)
- `StaleJobRecovery` after `LastKeepAlive` falls behind (handler must already be dead)

None of those bound the *runtime* of a healthy worker's handler. Result: a single bad payload can pin a worker slot indefinitely until someone notices.

A timeout enforces a maximum handler runtime per job. When elapsed exceeds the configured budget, the worker cancels the handler's `CancellationToken` and the job ends in `Deleted`. The mechanism reuses the existing graceful-cancellation contract that `[Mutex]`/`DeleteJob` already rely on (§Job Cancellation in `CLAUDE.md`).

## Solution

### Public API

```csharp
// Attribute — bound to the handler type. Default Mode = Delete.
[Timeout(seconds: 30)]
public class GenerateReport : IJob { }

// Attribute with explicit Mode — fail (and surface to retry) instead of delete.
[Timeout(seconds: 30, Mode = TimeoutMode.Fail)]
public class CallSlowApi : IJob { }

// Extension — per-publish override (wins over the attribute)
await publisher.Enqueue(
    new GenerateReport(),
    new JobParameters().WithTimeout(TimeSpan.FromMinutes(5)));

await publisher.Enqueue(
    new CallSlowApi(),
    new JobParameters().WithTimeout(TimeSpan.FromSeconds(30), TimeoutMode.Fail));

// Registration — addon, with optional global defaults applied when no attribute/extension is set
builder.Services.AddWarpWorker<AppDbContext>(opt =>
{
    opt.UsePostgreSql();
    opt.AddTimeout(o =>
    {
        o.Default = TimeSpan.FromMinutes(10);
        o.DefaultMode = TimeoutMode.Delete; // default; set to Fail for fleet-wide retry semantics
    });
});
```

Precedence (most specific wins): `WithTimeout` (extension at publish) → `[Timeout]` (handler attribute) → `TimeoutOptions.Default` (addon-level default) → no timeout. `Mode` follows the same precedence.

### `TimeoutMode`

```csharp
public enum TimeoutMode
{
    Delete = 1,  // pipeline sets Outcome { State = Deleted }; NOT retried by AddRetry
    Fail = 2,    // pipeline throws TimeoutException; AddRetry catches it like any other exception
}
```

Two modes, two intents:

- **`Delete`** — "kill this and move on." End state `Deleted`, audit row "Timed out after Xs". `ExpireAt` set by `FinalizeJobState`. Matches `DeleteJob` semantics — used by operators who want timeout to mean *abandoned*. AddRetry has no hook here (outcome path is invisible to the retry catch).
- **`Fail`** — "treat as a transient failure." Pipeline throws `TimeoutException`. If `AddRetry` is registered, retry behavior catches it and reschedules per `MaxRetries`/`Delays`. If retry is not registered or exhausts, the worker writes `Failed` (ExpireAt = null, surfaces in the Failed tab — §8.2). Matches Hangfire's default.

### `TimeoutScope`

```csharp
public enum TimeoutScope
{
    PerAttempt = 1,  // each attempt (initial + each retry) gets its own fresh timeout
    Total = 2,       // single deadline across the full retry chain
}
```

- **`PerAttempt`** (default) — the natural behaviour. Pipeline arms `CancelAfter(TimeoutSeconds)` on every attempt. A 3-retry job with 30s timeout can run for up to 4 × 30s wall-clock if every attempt times out.
- **`Total`** — the publish behaviour stamps `meta.DeadlineUtc = job.CreateTime + TimeoutSeconds` once at first publish. Pipeline reads `DeadlineUtc - now` per attempt. Past the deadline, the timer fires immediately (zero-delay) and the configured `Mode` runs. Deterministic across retries; predictable upper bound. Only useful with `Mode = Fail` (otherwise retries are off the table anyway). Combining `Total` + `Delete` is allowed but degenerate — equivalent to `PerAttempt` since there are no retries.

`DeadlineUtc` is anchored to `job.CreateTime` (not to first execution) because metadata mutations from inside the pipeline are not persisted across the exception path on all worker flows — anchoring at publish keeps it deterministic without new persistence wiring. Queue-time burn is the cost: a job queued for an hour with a 30-minute Total deadline will time out on first pickup. Documented; recommend `Total` only for jobs that pick up quickly relative to their timeout.

### `TimeoutAttribute`

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TimeoutAttribute : Attribute
{
    public TimeoutAttribute(int seconds)
    {
        if (seconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), "Timeout must be positive.");
        }

        Seconds = seconds;
    }

    public int Seconds { get; }

    public TimeoutMode Mode { get; init; } = TimeoutMode.Delete;

    public TimeoutScope Scope { get; init; } = TimeoutScope.PerAttempt;
}
```

Int seconds (not `TimeSpan`) because attribute constructors cannot take `TimeSpan` constants. Matches Hangfire's `[DisableConcurrentExecution(timeoutInSeconds)]` shape.

### `ITimeoutMetadata`

```csharp
public partial interface ITimeoutMetadata : IJobMetadata
{
    int? TimeoutSeconds { get; set; }

    TimeoutMode? Mode { get; set; }

    TimeoutScope? Scope { get; set; }

    DateTime? DeadlineUtc { get; set; }
}
```

Stored on the job's `Metadata` JSON — survives retries / requeues. The `Mode` and `Scope` are persisted so a job's behaviour on timeout doesn't drift if the addon's defaults change mid-flight. `DeadlineUtc` is populated by the publish behaviour only when `Scope = Total`.

### `WithTimeout` extension

```csharp
public static class TimeoutExtensions
{
    public static JobParameters WithTimeout(
        this JobParameters parameters,
        TimeSpan timeout,
        TimeoutMode mode = TimeoutMode.Delete,
        TimeoutScope scope = TimeoutScope.PerAttempt)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive.");
        }

        parameters.Configure<ITimeoutMetadata>(x =>
        {
            x.TimeoutSeconds = (int)Math.Ceiling(timeout.TotalSeconds);
            x.Mode = mode;
            x.Scope = scope;
        });

        return parameters;
    }
}
```

### `TimeoutOptions`

```csharp
public class TimeoutOptions
{
    public TimeSpan? Default { get; set; }

    public TimeoutMode DefaultMode { get; set; } = TimeoutMode.Delete;

    public TimeoutScope DefaultScope { get; set; } = TimeoutScope.PerAttempt;
}
```

### `TimeoutPublishBehavior<T>`

Cached per-type attribute lookup, sets metadata only when not already set (so `WithTimeout` wins). Applies `Default` last.

```csharp
public class TimeoutPublishBehavior<T> : IPublishPipelineBehavior<T>
{
    private static readonly ConcurrentDictionary<Type, TimeoutAttribute?> AttributeCache = new();
    private readonly IOptions<TimeoutOptions> _options;
    private readonly TimeProvider _timeProvider;

    public TimeoutPublishBehavior(IOptions<TimeoutOptions> options, TimeProvider timeProvider)
    {
        _options = options;
        _timeProvider = timeProvider;
    }

    public Task PublishAsync(PublishContext<T> context, PublishDelegate next, CancellationToken ct)
    {
        var meta = context.GetMetadata<ITimeoutMetadata>();

        if (meta.TimeoutSeconds == null)
        {
            var attr = AttributeCache.GetOrAdd(typeof(T), static t => t.GetCustomAttribute<TimeoutAttribute>());
            if (attr != null)
            {
                meta.TimeoutSeconds = attr.Seconds;
                meta.Mode ??= attr.Mode;
                meta.Scope ??= attr.Scope;
            }
            else if (_options.Value.Default is { } def)
            {
                meta.TimeoutSeconds = (int)Math.Ceiling(def.TotalSeconds);
                meta.Mode ??= _options.Value.DefaultMode;
                meta.Scope ??= _options.Value.DefaultScope;
            }
        }

        // Stamp a deterministic Total-scope deadline at publish time. Anchoring on TimeProvider
        // (not job.CreateTime, which is set by the publisher right before SaveChanges) is close
        // enough — they're set in the same scope, within milliseconds of each other.
        if (meta.TimeoutSeconds is { } secs
            && meta.Scope == TimeoutScope.Total
            && meta.DeadlineUtc == null)
        {
            meta.DeadlineUtc = _timeProvider.GetUtcNow().UtcDateTime.AddSeconds(secs);
        }

        return next();
    }
}
```

### `TimeoutPipelineBehavior<TRequest, TResponse>`

```csharp
public class TimeoutPipelineBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IJobContext _jobContext;
    private readonly TimeProvider _timeProvider;

    public TimeoutPipelineBehavior(IJobContext jobContext, TimeProvider timeProvider)
    {
        _jobContext = jobContext;
        _timeProvider = timeProvider;
    }

    public async Task<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TRequest, TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not IJob)
        {
            return await next(request, cancellationToken);
        }

        var meta = _jobContext.GetMetadata<ITimeoutMetadata>();
        if (meta.TimeoutSeconds is not { } seconds)
        {
            return await next(request, cancellationToken);
        }

        var scope = meta.Scope ?? TimeoutScope.PerAttempt;
        var mode = meta.Mode ?? TimeoutMode.Delete;
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        TimeSpan delay;
        if (scope == TimeoutScope.Total && meta.DeadlineUtc is { } deadline)
        {
            var remaining = deadline - now;
            delay = remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
        else
        {
            delay = TimeSpan.FromSeconds(seconds);
        }

        // TimeProvider.CreateCancellationTokenSource keeps the timer testable under FakeTimeProvider.
        using var cts = _timeProvider.CreateCancellationTokenSource(delay);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);

        try
        {
            return await next(request, linked.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            if (mode == TimeoutMode.Fail)
            {
                throw new TimeoutException($"Job timed out after {seconds}s");
            }

            _jobContext.Outcome = new JobOutcome
            {
                State = State.Deleted,
                LogMessage = $"Timed out after {seconds}s",
            };

            return default!;
        }
    }
}
```

**`stats:timeout` counter deferred to v1.1.** The initial design wrote the counter via an `ITimeoutCounterSink` that opened a fresh DbContext scope per timeout fire. Under integration-test load this saturated the Postgres test container (`53300: sorry, too many clients already` from `MaxPoolSize`-bounded pools). The right plumbing is to set a `TimedOut` flag on `IJobContext` and let the worker emit the counter from its own context (success and failure paths both read the flag before disposing handler scope). That worker-side wiring is straightforward but invasive enough that we left it for v1.1; v1 ships the audit log only. Operators can still answer "is this job timing out?" via the per-job "Timed out after Xs" log entry.

The catch guard `cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested` distinguishes the *timer* firing from worker shutdown — only the timer should be reported as a timeout. If the worker itself is shutting down, the OCE propagates out unchanged.

In `Fail` mode the pipeline rethrows `TimeoutException`. If `AddRetry` is registered, `RetryPipelineBehavior` catches the exception and reschedules per its configuration. If no retry is registered, the worker's existing exception handler writes `Failed` (ExpireAt = null per §8.2).

### `TimeoutServiceConfiguration`

```csharp
public static class TimeoutServiceConfiguration
{
    public static IWarpBuilder<TContext> AddTimeout<TContext>(
        this IWarpBuilder<TContext> builder,
        Action<TimeoutOptions>? configure = null)
        where TContext : DbContext
    {
        if (configure != null)
        {
            builder.Services.Configure(configure);
        }
        else
        {
            builder.Services.AddOptions<TimeoutOptions>();
        }

        builder.Services.AddTransient(typeof(IPublishPipelineBehavior<>), typeof(TimeoutPublishBehavior<>));
        builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TimeoutPipelineBehavior<,>));

        return builder;
    }
}
```

### Behavioral contract

1. Handler honours the cancellation token → throws `OperationCanceledException` → pipeline catches:
   - `Mode = Delete` → sets `Outcome { State = Deleted, LogMessage = "Timed out after Xs" }`. Worker `FinalizeJobState` writes the audit row. ExpireAt set.
   - `Mode = Fail` → throws `TimeoutException`. With `AddRetry`: retried per config. Without: worker writes `Failed` (ExpireAt = null).
2. Handler ignores the token and completes successfully → same as today's graceful-cancellation flow for ignored tokens (§Job Cancellation step 4 in `CLAUDE.md`): job ends `Completed`. Operators relying on timeout should write cancellable handlers — same guidance as `DeleteJob`.
3. Worker shutdown during a timed-out wait → OCE propagates (the `!cancellationToken.IsCancellationRequested` guard fails), the worker's existing shutdown path runs (job stays `Processing`, `StaleJobRecovery` will reset it). No spurious timeout outcome.
4. Retry interaction:
   - `Mode = Delete` + `AddRetry`: timed-out job is NOT retried (outcome bypasses retry's catch). Intentional.
   - `Mode = Fail` + `AddRetry`: timed-out job IS retried as a `TimeoutException`. Each retry attempt gets its own timeout (the metadata stays the same across attempts).
   - `Mode = Fail` without `AddRetry`: timed-out job ends `Failed`.
5. Pipeline registration order: `RetryPipelineBehavior` should wrap `TimeoutPipelineBehavior`. Both register as `Transient` and the framework resolves them in registration order — `AddRetry()` MUST be called before `AddTimeout()` if both are registered, so that retry's catch sees the timeout's throw. Documented; tests assert it.

### Telemetry

Pipeline already opens an activity around the handler; no new spans. Outcome adds an event `warp.job.deleted` via the existing worker code (outcome.State.ToString().ToLowerInvariant()). Optional follow-up: a dedicated `warp.job.timeout` counter — out of v1 scope.

## Out of scope

- **Hard kill** (forcibly killing a non-cancellable handler). Not safely achievable from inside the worker process on modern .NET: `Thread.Abort` was removed in .NET 5 because it corrupts invariants of whatever the killed thread was touching (locks, transactions, half-written state); `Thread.Interrupt` only unblocks `WaitSleepJoin`, not CPU-bound or P/Invoke work; AppDomain unload is gone. The realistic alternatives are (a) worker process recycle, which `StaleJobRecovery` already handles (operator restarts the worker; jobs without `LastKeepAlive` get re-enqueued), or (b) process-per-handler isolation, which is a separate worker-architecture feature ("Isolated workers"), not a timeout flag. The cooperative-cancellation path is the only safe option for the timeout addon. Documented in `timeout.md` with explicit guidance: "your handler must honour the cancellation token; if it can't, escalate to a worker restart."
- **Timeout for `IRequest<T>` / `IStreamRequest<T>` in-memory paths.** Pipeline is shared, but in-memory callers usually wrap their own `CancellationToken`. The `request is not IJob` bail-out keeps the addon job-only.
- **Dedicated "Timeouts" Jobs tab** (filterable list of timed-out jobs). v1 ships the counter; a filtered job list requires new query plumbing (timed-out-recently filter) — defer until operators ask. Audit log already shows "Timed out after Xs" on each job.
- **Deadline anchored to first execution** instead of `CreateTime`. Rejected — would require persisting deadline through the exception/retry path which isn't free, and `CreateTime` is close enough for the "Total" intent. Documented as a known limitation for slow-queueing scenarios.

## Risks

- **Handlers that ignore the token** — same risk as `DeleteJob` today. Surface in docs: "your handler must honour the cancellation token for the timeout to take effect."
- **Misinterpretation of `Default`** — operators may set a global default that surprises handlers without explicit timeouts. Documented as opt-in only; addon has no default-default.
- **Clock skew during long timeouts** — `CancellationTokenSource.CancelAfter` uses `Environment.TickCount`, not the injected `TimeProvider`. Acceptable for v1 since the timer doesn't need to be testable through `FakeTimeProvider` — we test via real wall-clock with short timeouts. Documented as a known limitation; if FakeTimeProvider testing becomes important, swap to `TimeProvider.CreateCancellationTokenSource(delay)` (.NET 8+).

  Updating: actually `TimeProvider.CreateCancellationTokenSource(TimeSpan delay)` *exists* on .NET 8+, and we target .NET 10. Use it. This makes the timeout testable against `FakeTimeProvider` (advance time → CTS fires) without real waits.

## Verification

- `[Timeout(1)]` on a handler that sleeps 5s → job ends `Deleted` with "Timed out after 1s" log.
- `[Timeout(1, Mode = TimeoutMode.Fail)]` on a handler that sleeps 5s, no `AddRetry` → job ends `Failed` with `TimeoutException` message.
- `[Timeout(1, Mode = TimeoutMode.Fail)]` + `AddRetry(o => o.MaxRetries = 2)` → job retried twice, then ends `Failed`. Each attempt times out independently (PerAttempt scope).
- `[Timeout(2, Mode = TimeoutMode.Fail, Scope = TimeoutScope.Total)]` + `AddRetry(o => o.MaxRetries = 5)` with a handler that always sleeps past timeout → total wall-clock from CreateTime to terminal state ≤ 2s + retry-backoff overhead; far fewer attempts than `MaxRetries + 1`. Final state `Failed`.
- `WithTimeout(TimeSpan.FromSeconds(1))` overrides `[Timeout(60)]` on the same handler.
- `WithTimeout(TimeSpan.FromSeconds(1), TimeoutMode.Fail)` overrides attribute's `Mode = Delete`.
- `AddTimeout(o => o.Default = TimeSpan.FromSeconds(1))` applies to a handler with no attribute/extension; defaults to `Mode = Delete`, `Scope = PerAttempt`.
- `AddTimeout(o => { o.Default = ...; o.DefaultMode = TimeoutMode.Fail; o.DefaultScope = TimeoutScope.Total; })` applies Fail-Total defaults fleet-wide.
- No timeout configured → handler with `Task.Delay(2000)` completes normally (no regression).
- Handler that ignores the cancellation token → ends `Completed` (the documented "misbehaving handler" path), regardless of mode.
- Worker shutdown while a job is mid-timeout → OCE propagates normally; no spurious "Timed out" outcome.
- `TimeoutAttribute(0)` / negative throws at construction. `WithTimeout(TimeSpan.Zero)` throws.
- Full timeout test suite passes on both PostgreSQL and SQL Server matrices (36 tests: 27 NoDb + 5 × 2 integration).

## Scope classification

**Small feature.** Single addon namespace, ~7 new files, ~2 modifications, ~9 tests. No new entities, no schema change, no dashboard work, no provider work. Inline implementation path (manifest < 6 files of new code, but tests push above — still inline per `security_impact = none`).

## Implementation batches

1. **Core addon** — `TimeoutAttribute`, `TimeoutMode`, `TimeoutScope`, `ITimeoutMetadata`, `TimeoutOptions`, `WithTimeout` extension, `TimeoutPublishBehavior`, `TimeoutPipelineBehavior`, `TimeoutServiceConfiguration`. Use `TimeProvider.CreateCancellationTokenSource(delay)` for FakeTimeProvider testability. Inject `TimeProvider` into both behaviors. Emit `stats:timeout` / `stats:timeout:{hour}` counters from the pipeline behavior on timer fire (writing via the same path used by `FinalizeJobState` — `context.Set<Counter>().Add(...)`, with a small ICounterRecorder seam if needed to avoid leaking `TContext` into the behavior).
2. **Tests — NoDb attribute/extension** — `TimeoutAttribute_NonPositive_Throws`, `WithTimeout_NonPositive_Throws`, `WithTimeout_SetsAllFields`, attribute defaults assert `Mode = Delete`, `Scope = PerAttempt`.
3. **Tests — unit-style against real DB** (`Features/Timeout/TimeoutTests.cs`) — publish + pipeline behaviour in isolation:
   - `PublishBehaviour_AppliesAttribute_WhenMetadataMissing`
   - `PublishBehaviour_WithTimeoutWinsOverAttribute`
   - `PublishBehaviour_DefaultAppliedWhenNoAttribute` (Mode + Scope)
   - `PublishBehaviour_TotalScope_StampsDeadlineAtPublish`
   - `PublishBehaviour_PerAttemptScope_DeadlineRemainsNull`
   - `PipelineBehaviour_NoTimeoutMetadata_PassesThrough`
   - `PipelineBehaviour_DeleteMode_HandlerHonoursToken_OutcomeDeleted`
   - `PipelineBehaviour_FailMode_HandlerHonoursToken_ThrowsTimeoutException`
   - `PipelineBehaviour_TotalScope_RemainingTimeUsed`
   - `PipelineBehaviour_TotalScope_DeadlinePast_FiresImmediately`
   - `PipelineBehaviour_WorkerShutdownPropagates_NoTimeoutOutcome`
   - `PipelineBehaviour_OnFire_IncrementsTimeoutCounter` (Delete and Fail variants)
4. **Tests — integration** (`Features/Timeout/TimeoutIntegrationTests.cs`, `[GenerateDatabaseTests(FixtureKind.Integration)]`) — E2E:
   - `DeleteMode_JobExceedsTimeout_EndsDeleted_WithTimeoutLog`
   - `DeleteMode_PlusAddRetry_TimedOutNotRetried`
   - `FailMode_NoRetry_EndsFailed_WithTimeoutException`
   - `FailMode_PlusAddRetry_PerAttempt_IsRetried_ThenFails`
   - `FailMode_PlusAddRetry_TotalScope_BoundsTotalWallClock`
5. **Docs** — `website/docs/features/timeout.md` (new, ~120 lines covering both modes and scopes, with examples for the four combinations and guidance on `AddRetry()` registration order), `CLAUDE.md` addon list line, mention of `stats:timeout` in the Counters section if present.
6. **ROADMAP.md** — strike out the Job Timeout entry (matches the Mutex/Pause precedent).

## Public contracts

New surface (additive, in new namespace `Warp.Core.Timeout`):

- `TimeoutAttribute` (with `Mode`, `Scope` init-only properties)
- `TimeoutMode` (Delete = 1, Fail = 2)
- `TimeoutScope` (PerAttempt = 1, Total = 2)
- `ITimeoutMetadata` (with `TimeoutSeconds`, `Mode`, `Scope`, `DeadlineUtc`)
- `TimeoutOptions` (with `Default`, `DefaultMode`, `DefaultScope`)
- `TimeoutExtensions.WithTimeout(this JobParameters, TimeSpan, TimeoutMode = Delete, TimeoutScope = PerAttempt)`
- `TimeoutPublishBehavior<T>`
- `TimeoutPipelineBehavior<TRequest, TResponse>`
- `TimeoutServiceConfiguration.AddTimeout`

No renames, no breaking changes. `stats:timeout` counter is deferred to v1.1 (see Solution > Telemetry note).

## Open decisions

None — confirm at the approval gate.
