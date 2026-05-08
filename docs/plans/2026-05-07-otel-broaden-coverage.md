# Plan: OpenTelemetry — Spec-Compliant Coverage

Spec: [`docs/specs/2026-05-07-otel-broaden-coverage.md`](../specs/2026-05-07-otel-broaden-coverage.md).

Anchor on the OTel messaging convention. No invented surface.

## Design decisions (and what we rejected)

### Single ActivitySource + single Meter, both named "Warp"

Already exists. Wiring stays one line: `tracerBuilder.AddSource("Warp")` + `meterBuilder.AddMeter("Warp")`. **Rejected:** sub-sources like `Warp.Mediator` / `Warp.Worker` (forces users to register multiple sources for one library; the OTel-conventional shape is one source per library).

### Span name format `<operation> <destination>` per OTel spec

Stable in `https://opentelemetry.io/docs/specs/semconv/messaging/messaging-spans/`. We use `send <queue>`, `receive <queue>`, `process <queue>` for jobs, and `process <RequestType>` for the in-memory mediator (where the "destination" is the type name). **Rejected:** keeping `Warp.Execute` (non-conformant; opaque to OTel-native filters on `messaging.operation.name`).

### Producer span emits, but is NOT the consumer's parent

`Activity.Current?.SpanId` is captured into `Job.ParentSpanId` *before* the producer span is started. So consumer span's parent is still the caller (HTTP request, parent handler), and the producer span is a sibling event marker on the caller's trace. **Why:** it's what users actually want — see "publish happened here" without burying the consumer one level deeper. **Rejected:** parenting consumer to producer (turns every trace into a deep chain of one-tick producer spans; unhelpful for ops dashboards).

### Receive span = Client kind, not Consumer

Per OTel spec, Consumer-kind is reserved for "the operation that processes the message." Worker fetch/dequeue is a client of the queue (not yet processing), so it's `messaging.operation.type=receive` with kind `Client`. **Rejected:** Consumer kind on receive (spec-discouraged).

### Mediator span uses messaging conventions; treat RequestType as destination

`Mediator.Send(GetUser)` emits `process GetUser` with `messaging.destination.name = GetUser` and `warp.mediator.kind=request`. **Why:** the request type is the "where" — it's what handlers route on, and it lets OTel-native filtering on `messaging.destination.name` work for in-process routing. **Rejected:** custom span names like `Warp.Mediator.Send` — non-conformant with the OTel `<operation> <destination>` shape and won't be picked up by collectors' built-in messaging dashboards.

### Library-prefixed metric names (`warp.*`), not OTel-stable `messaging.client.*`

Library-prefixed metric names give operators a stable, easily-filterable namespace and avoid version-pinning to the spec-stable `messaging.client.sent.messages` family (which is still under churn in the OTel spec). We keep `warp.job.duration`, `warp.job.completed`, `warp.job.enqueued`, `warp.job.active`, and add `warp.mediator.duration`, `warp.mediator.in_flight`. **Rejected:** rename to `messaging.client.*` / `messaging.process.duration` — would couple our metric API to a moving spec and force users to migrate dashboards on each rev.

### `messaging.operation.type` and `messaging.operation.name` both set

Spec splits low-cardinality verb (`type`) from free-form (`name`). Existing code only sets `.name`. Setting both is single tag-add, no semantic change. **Rejected:** setting only one (forwards-incompatible if the convention deprecates `.name` later).

### Library-prefixed attribute namespace (`warp.*`) for non-messaging concerns

`warp.task.name`, `warp.task.lock_held`, `warp.mutex.key`, `warp.mutex.acquired`, `warp.mediator.kind`, `warp.job.attempt`, `warp.worker.id`, `warp.worker.group`. **Rejected:** putting these under `messaging.warp.*` (hijacks the `messaging.*` namespace for non-messaging concepts and would cause collector dashboards to misclassify them as message-passing operations).

### One breaking change: consumer span name

`Warp.Execute` → `process <queue>`. Released in 0.12.0 (very recent), unlikely to be in production exporter pipelines yet. Release notes call it out. **Rejected:** keeping `Warp.Execute` for back-compat (perpetuates a non-conformant name; OTel collectors don't recognize it for messaging dashboards).

## Architecture sketch

```
Caller (Activity = "GET /orders", set by ASP.NET)
  │
  ├─ Publisher.Enqueue/Publish/Schedule
  │     ├─ snapshot Activity.Current.SpanId → local
  │     ├─ Activity "send <queue>" (Producer)
  │     │     tags: messaging.* + warp.job.*
  │     │     span ends as the row is added (one-tick lifetime)
  │     └─ EF: Job row inserted (ParentSpanId = caller's snapshot, NOT producer's)
  │
  └─ (returns to caller's span)

Worker.ProcessJob (after fetch)
  Activity "receive <queue>" (Client)
    tags: messaging.* + warp.worker.id
    (closes before consumer span opens — sibling, not parent)

  Activity "process <queue>" (Consumer, parent = caller's snapshot)
    tags: messaging.* + warp.job.* + warp.retry.attempt + warp.worker.*
    │
    └─ pipeline behaviors:
          ├─ MutexPipelineBehavior
          │     └─ Activity "warp.mutex_acquire" (Internal, child of consumer)
          │           tags: warp.mutex.key, warp.mutex.acquired
          │           closes before next() so the handler runs under consumer
          └─ Handler.HandleAsync()

App calls IMediator.Send(GetUser(1))
  Activity "process GetUser" (Internal)
    tags: messaging.system=warp, operation.*=process, destination.name=GetUser, warp.mediator.kind=request
    metrics: warp.mediator.in_flight ±1, warp.mediator.duration on close
    │
    └─ IPipelineBehavior chain → IRequestHandler.HandleAsync()

ServerTaskLoop.RunOneIterationAsync (per server task per tick)
  Activity "warp.server_task <Name>" (Internal)
    tags: warp.task.name, warp.task.lock_held, warp.task.message
    │
    └─ IServerTask.ExecuteAsync()
```

## File-by-file change summary

| File | Change |
|---|---|
| `src/core/Warp.Core/Logging/WarpTelemetry.cs` | Add `WarpTelemetryAttributes` constants. Add `MediatorDuration`, `MediatorInFlight` instruments. Add `StartProducerActivity`, `StartReceiveActivity`, `StartMediatorActivity`, `StartServerTaskActivity`, `StartMutexActivity` helpers. Update `StartJobActivity` overload to take `queue` and use `process <queue>` as span name; set `operation.type=process`, `conversation_id`. |
| `src/core/Warp.Core/Publisher.cs` | `Publish` (line ~75) and `CreateJob` (line ~170): snapshot caller's `Activity.Current.SpanId` first, then `using var producerSpan = WarpTelemetry.StartProducerActivity(queue, "send", kind)`, set messaging tags after `msg.Id` known. |
| `src/core/Warp.Core/BatchPublisher.cs` | `StartNew`: snapshot caller's span, start producer span, tag `messaging.batch.message_count = batchChildJobs.Count`. |
| `src/core/Warp.Core/Handlers/MediatorDispatcher.cs` | `ExecuteTypedPipeline` and `ExecuteTypedStreamPipeline`: wrap pipeline+handler in mediator activity + in-flight gauge ±1 + duration histogram on close. Stream version: `try/finally` around iterator, capture activity into `UnwrapStreamTask`. |
| `src/core/Warp.Core/Mutex/MutexPipelineBehavior.cs` | After early returns, wrap `TryAcquireAsync` in `warp.mutex_acquire` Internal activity. Stamp `warp.mutex.key`, `warp.mutex.acquired`. Activity closes before `next()`. |
| `src/core/Warp.Worker/WarpWorkerService.cs` | Open `receive <queue>` Client span around lines 85–101 (mark ownership, write Processing log). After deserializing metadata (line ~138), set `warp.job.attempt`, `warp.worker.id`, `warp.worker.group` on consumer span. Add `error.type` on catch path. |
| `src/core/Warp.Worker/WarpDispatcherWorker.cs` | Same receive-span + retry/worker tags + error.type pattern as `WarpWorkerService`. |
| `src/core/Warp.Worker/Services/ServerTaskLoop.cs` | In `RunOneIterationAsync` (line ~181): wrap `ExecuteInLockedScopeAsync` call in `warp.server_task <Name>` Internal activity. Stamp `warp.task.lock_held` from the return-value-vs-skipped distinction; `error.type` on the catch path. |
| `src/tests/Warp.Tests/TestData/Helpers/ActivityListenerHarness.cs` | New: thin helper that registers an `ActivityListener` for source `"Warp"`, captures stops, exposes `IReadOnlyList<Activity> Captured`. |
| `src/tests/Warp.Tests/Observability/ProducerSpanTests.cs` | New, NoDb. Per-publish-method emission, tag correctness, parent ordering invariant, listener-off no-op. |
| `src/tests/Warp.Tests/Observability/ReceiveSpanTests.cs` | New, PG + SQL integration. Receive-then-process ordering, shared trace id. |
| `src/tests/Warp.Tests/Observability/MediatorSpanTests.cs` | New, NoDb. Send + CreateStream + exception + cancellation; in-flight + duration; stream activity spans full enumeration. |
| `src/tests/Warp.Tests/Observability/MutexSpanTests.cs` | New, NoDb. Acquired vs held; bypass paths emit no span. |
| `src/tests/Warp.Tests/Observability/ServerTaskSpanTests.cs` | New, PG + SQL integration. Per-iteration span; lock_held correct on race; error path stamps error.type. |
| `src/tests/Warp.Tests/Observability/RetryAttemptTagTests.cs` | New, PG + SQL integration. `warp.job.attempt=1` first run, `=2` after retry. |
| `src/tests/Warp.Tests/Observability/ActivityTraceTests.cs` | Update span-name expectations from `Warp.Execute` to `process <queue>`. |
| `src/tests/Warp.Tests/Observability/WarpTelemetryTests.cs` | Add unit coverage for the five new helpers. |

Total: 9 source files modified, 7 test files (6 new + 2 modified existing). 14 distinct files.

## Implementation batches

### Batch 1 — Telemetry helpers and constants

**Files:** `WarpTelemetry.cs`, `WarpTelemetryAttributes.cs` (new), `WarpTelemetryTests.cs`, `ActivityListenerHarness.cs` (new).

Add the OTel attribute key constants (`WarpTelemetryAttributes.MessagingSystem`, etc.) so we never typo `messaging.operation.name`. Add `MediatorDuration` and `MediatorInFlight` instruments. Add the five new `Start*Activity` helpers. Add an overload of `StartJobActivity(traceId, parentSpanId, queue)` that uses `process <queue>` as the span name and sets the new tags; keep the existing two-arg overload as a thin shim calling the new one with `queue = "default"` for safety.

`ActivityListenerHarness` lives under `TestData/Helpers/` (mirrors existing `Helpers/` pattern). Listens to source `"Warp"`, captures activities on stop into a thread-safe list, exposes `Captured` and `IDisposable` cleanup.

`WarpTelemetryTests` gets one test per new helper (null when no listener, non-null with correct kind/name when listener attached).

**Checkpoint:** `dotnet build Warp.slnx`; `dotnet test --project tests/Warp.Tests/Warp.Tests.csproj -- --filter-trait "Category=NoDb"`.

### Batch 2 — Producer spans + consumer rename

**Files:** `Publisher.cs`, `BatchPublisher.cs`, `WarpWorkerService.cs`, `WarpDispatcherWorker.cs`, `ProducerSpanTests.cs` (new), `ActivityTraceTests.cs` (existing — span-name update).

In `Publisher.Publish` and `Publisher.CreateJob`, capture `Activity.Current?.SpanId` into a local first (this preserves the existing `ParentSpanId` semantics), then `using var producerSpan = WarpTelemetry.StartProducerActivity(...)`. Tag set after `msg.Id`/`newJob.Id` is known. Same pattern in `BatchPublisher.StartNew` plus `messaging.batch.message_count`.

In both worker classes, switch the consumer-side `StartJobActivity` call to the new overload and rely on the helper to set `process <queue>` as the span name. Tags on the consumer activity gain `messaging.operation.type`, `messaging.message.conversation_id`, plus `messaging.batch.message_count` when `job.Kind == Batch`. Add `error.type` on the catch path.

`ProducerSpanTests` (NoDb): for each publish method, assert exactly one `send <queue>` Producer span; assert all required messaging tags; assert `Job.ParentSpanId == caller.SpanId` (the parent-vs-producer ordering invariant); assert no spans when no listener.

Update `ActivityTraceTests` span-name expectations.

**Checkpoint:** NoDb suite green; `ActivityTraceTests` PG+SQL pass.

### Batch 3 — Receive span + mediator coverage

**Files:** `WarpWorkerService.cs`, `WarpDispatcherWorker.cs`, `MediatorDispatcher.cs`, `ReceiveSpanTests.cs` (new), `MediatorSpanTests.cs` (new).

In each worker's `ProcessJob`, after the row is fetched and before `StartJobActivity`, open a Client-kind `receive <queue>` span around the post-fetch bookkeeping (mark ownership, write Processing log row, save). Span closes before the consumer span opens — sibling under the producer's parent (caller's span).

In `MediatorDispatcher.ExecuteTypedPipeline`, wrap the chain construction + execution in a mediator activity and `MediatorInFlight ±1` + `MediatorDuration` record on close. Exception path: set `Status=Error`, `error.type`, status tag = `failed`. Cancellation: status = `cancelled`. Stream version: capture the activity into the iterator state in `UnwrapStreamTask`'s `try/finally` so it stops on enumerator dispose / completion.

`ReceiveSpanTests` (PG + SQL): publish a job, wait for completion, assert receive precedes process under the same trace id, durations sane.

`MediatorSpanTests` (NoDb): full matrix — request happy path, request exception, request cancellation, stream happy path with multi-item, stream exception mid-enumeration, stream cancellation, in-flight gauge, duration histogram tag set.

**Checkpoint:** NoDb green; `ReceiveSpanTests` green on at least one provider locally.

### Batch 4 — Mutex + retry + server-task

**Files:** `MutexPipelineBehavior.cs`, `WarpWorkerService.cs` (retry tag), `WarpDispatcherWorker.cs` (retry tag), `ServerTaskLoop.cs`, `MutexSpanTests.cs` (new), `RetryAttemptTagTests.cs` (new), `ServerTaskSpanTests.cs` (new).

`MutexPipelineBehavior.HandleAsync`: after early returns (`request is not IJob`, `ConcurrencyKey == null`), open `warp.mutex_acquire` Internal span around `TryAcquireAsync`. Stamp `warp.mutex.key`, `warp.mutex.acquired`. Span closes once acquire result is known — handler runs under the parent (consumer) span, not under the mutex span. On held-by-other path, add an `ActivityEvent("warp.mutex.held_by_other")` before short-circuiting.

In both workers, after metadata is deserialized into `jobContext.Metadata`, read `IRetryMetadata.RetriedTimes` via the existing accessor and set `warp.job.attempt = retriedTimes + 1` on the consumer activity. Optional `warp.job.max_attempts` from `IRetryMetadata.MaxRetries` when set.

`ServerTaskLoop.RunOneIterationAsync`: wrap `ExecuteInLockedScopeAsync` call in `warp.server_task <Name>` Internal activity. Stamp `warp.task.lock_held` based on whether the scope ran (we need to surface that info up — small refactor to `ExecuteInLockedScopeAsync` signature: return a struct `(bool LockHeld, string? Message)` instead of `string?`). Tag `warp.task.message` on success, `error.type` on failure.

`MutexSpanTests` (NoDb), `RetryAttemptTagTests` (PG + SQL), `ServerTaskSpanTests` (PG + SQL).

**Checkpoint:** full test suite green on at least one provider locally; build clean.

## Tests directory layout

```
src/tests/Warp.Tests/
├── Observability/
│   ├── ActivityTraceTests.cs                 [modified — span name update]
│   ├── DashboardBreakdownTests.cs            [unchanged]
│   ├── HourlyStatsTests.cs                   [unchanged]
│   ├── JobLogCollectorTests.cs               [unchanged]
│   ├── JobLogTests.cs                        [unchanged]
│   ├── MediatorSpanTests.cs                  [new]
│   ├── MutexSpanTests.cs                     [new]
│   ├── OTelMetricsTests.cs                   [unchanged]
│   ├── ProducerSpanTests.cs                  [new]
│   ├── RealTimeLogIntegrationTests.cs        [unchanged]
│   ├── ReceiveSpanTests.cs                   [new]
│   ├── RetryAttemptTagTests.cs               [new]
│   ├── ServerTaskSpanTests.cs                [new]
│   ├── SpanPropagationTests.cs               [unchanged]
│   ├── StatCounterTests.cs                   [unchanged]
│   ├── StatsTests.cs                         [unchanged]
│   ├── TraceIntegrationTests.cs              [unchanged]
│   ├── TracePropagationIntegrationTests.cs   [unchanged]
│   ├── WarpTelemetryTests.cs                 [modified — helper coverage]
│   └── WorkerIdLogTests.cs                   [unchanged]
└── TestData/Helpers/
    └── ActivityListenerHarness.cs            [new]
```

## Verification before merge

1. `dotnet build Warp.slnx` — zero warnings (StyleCop / Roslynator / Sonar / Meziantou).
2. `dotnet test --project tests/Warp.Tests/Warp.Tests.csproj` — full suite green on PG + SQL locally.
3. Manual span check: run the demo app (`src/demo/Warp.TestApp/`) with OTel console exporter, publish a few jobs, eyeball the span tree — producer → receive sibling + process child, mediator span on the test app's mediator calls, server-task spans for each iteration.
4. Pre-commit-review-list scan if present.
5. Update `website/docs/releases.md` with a "Breaking change: consumer span name" callout for the next version (0.13.0 or whatever is next).
