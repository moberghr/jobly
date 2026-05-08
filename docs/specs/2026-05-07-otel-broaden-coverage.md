# Spec: OpenTelemetry — Spec-Compliant Coverage

## Problem

Warp ships a partial OTel surface today: `WarpTelemetry.ActivitySource = "Warp"`, `WarpTelemetry.Meter = "Warp"`, a `Warp.Execute` consumer span carrying messaging-convention tags (`messaging.system`, `messaging.operation.name`, `messaging.destination.name`, `messaging.message.id`), and counters/histograms (`warp.job.duration` ms, `warp.job.active`, `warp.job.completed`, `warp.job.enqueued`, `warp.notifications.*`).

Three gaps vs. the OTel messaging spec:

1. **No producer span.** Publishers capture `Activity.Current?.SpanId` into `Job.ParentSpanId` but never emit their own activity. OTel messaging conventions call for a Producer-kind span at publish time. Without it, a distributed trace shows no breadcrumb between "caller" and "consumer", and operators can't attribute publish latency.
2. **No receive span.** Worker fetches a row from the DB and opens the consumer span directly. OTel messaging conventions split `receive` (Client-kind dequeue) from `process` (Consumer-kind handler invocation).
3. **In-process mediator and server-task work is invisible.** `IMediator.Send`, `IMediator.CreateStream`, every `IServerTask` (Heartbeat, Orchestrator, MessageRouter, RecurringJobScheduler, …), and pipeline-behavior decisions (mutex held, retry attempt) emit no activities.

We also have two minor compliance issues on the existing consumer span:

- Span name is `Warp.Execute` — OTel spec requires `<operation.name> <destination>` (e.g. `process default`).
- Tag `messaging.operation.type` is missing (spec splits `messaging.operation.name` free-form from `messaging.operation.type` low-cardinality verb; both should be set).

This spec is anchored on the OTel messaging convention (`https://opentelemetry.io/docs/specs/semconv/messaging/messaging-spans/`). **No invented surface.**

## Solution

Three operations get spans, all on the existing `WarpTelemetry.ActivitySource = "Warp"` (single source, single meter — gives users a one-line wiring story `tracerBuilder.AddSource("Warp")`). Span name format `"<operation.name> <destination>"` per spec.

### 1. Producer spans on publish

`Publisher.Publish` (IMessage), `Publisher.CreateJob` (covers Enqueue + Schedule for IJob), `BatchPublisher.StartNew` each emit a Producer-kind span.

| Operation         | Span name        | Kind     | `operation.name` | `operation.type` |
|-------------------|------------------|----------|------------------|------------------|
| `Publisher.Publish` | `send <queue>` | Producer | `send`           | `send`           |
| `Publisher.Enqueue` / `Schedule` | `send <queue>` | Producer | `send` | `send` |
| `BatchPublisher.StartNew` | `send <queue>` | Producer | `send` | `send` |

Tags on every producer span:

```
messaging.system                  = "warp"
messaging.operation.name          = "send"
messaging.operation.type          = "send"
messaging.destination.name        = job.Queue
messaging.message.id              = job.Id (Guid string)
messaging.message.conversation_id = job.TraceId (Guid string)
messaging.batch.message_count     = N                      (BatchPublisher only)
warp.job.kind                     = "Job" | "Message" | "Batch"
warp.job.type                     = short type name
warp.job.scheduled                = true|false             (Schedule path → true)
```

**Critical ordering**: `Activity.Current?.SpanId` is captured into `Job.ParentSpanId` *before* the producer span is started, so the consumer's parent is still the caller (HTTP request, parent handler), not the one-tick producer span. The producer span is an event marker, not the consumer's parent.

`messaging.client.sent.messages` (the OTel-stable counter name) is **not** added — Warp already ships `warp.job.enqueued`. Library-prefixed metric names (rather than the spec-stable `messaging.client.*` family) are the de-facto convention in this space. We keep `warp.job.enqueued` as the per-publish counter.

### 2. Receive span on worker fetch

The worker pulls a row, then opens a Client-kind `receive <queue>` span around the post-fetch bookkeeping (mark ownership, write Processing log row, etc.) — the work that happens between DB fetch and handler invocation. The span closes before the consumer span opens, so receive and process are siblings under the producer's parent (the caller's span).

| Where | Span name | Kind | `operation.name` | `operation.type` |
|---|---|---|---|---|
| `WarpWorkerService.ProcessJob` (post-fetch, pre-`StartJobActivity`) | `receive <queue>` | Client | `receive` | `receive` |
| `WarpDispatcherWorker.ProcessJob` (same point) | `receive <queue>` | Client | `receive` | `receive` |

Tags:

```
messaging.system            = "warp"
messaging.operation.name    = "receive"
messaging.operation.type    = "receive"
messaging.destination.name  = job.Queue
messaging.message.id        = job.Id
warp.worker.id              = workerId
```

### 3. Consumer-span alignment + retry tag

Existing `Warp.Execute` span renamed to `process <queue>` per OTel format. Tags already set today are kept; we add:

```
messaging.operation.type          = "process"           (alongside existing .name)
messaging.message.conversation_id = job.TraceId
messaging.batch.message_count     = N                   (only when job.Kind == Batch)
error.type                        = exception type FQN  (failure path)
warp.job.attempt                  = retry count + 1     (1-based; from IRetryMetadata)
warp.worker.id                    = workerId
warp.worker.group                 = worker group name (when set)
```

This is a span-name change. We accept the breaking-change cost on the 0.12.x → 0.13 line — `Warp.Execute` was added in the recent telemetry work and is not yet a stable contract used in production exporters. Release notes call out the rename. Test asserts in this repo are updated in-batch.

### 4. Mediator coverage

`MediatorDispatcher.ExecuteTypedPipeline` (request) and `ExecuteTypedStreamPipeline` (stream) emit Internal-kind spans named `process <RequestType>` — the request type is treated as the destination name so OTel-native filtering on `messaging.destination.name` works for in-process routing. Tags:

```
messaging.system            = "warp"
messaging.operation.name    = "process"
messaging.operation.type    = "process"
messaging.destination.name  = TRequest short type name
warp.mediator.kind          = "request" | "stream"
warp.mediator.response_type = TResponse short type name
error.type                  = exception type FQN  (failure path)
```

Two new instruments on `WarpTelemetry.Meter`:

- `warp.mediator.duration` — Histogram&lt;double&gt;, ms, tags `kind` (`request`/`stream`), `request_type`, `status` (`succeeded`/`failed`/`cancelled`)
- `warp.mediator.in_flight` — UpDownCounter&lt;long&gt;, tags `kind`, `request_type`

Stream-activity lifetime: the activity is captured into the iterator state and disposed on enumerator dispose / completion / abandonment. Test guards this.

### 5. Server-task and mutex-acquire spans

`ServerTaskLoop<TContext>.RunOneIterationAsync` wraps `ExecuteInLockedScopeAsync` in an Internal-kind span. `MutexPipelineBehavior.HandleAsync` wraps the `TryAcquireAsync` call in an Internal-kind span (closes before `next()` so the handler runs under the consumer span, not under the mutex span).

| Where | Span name | Kind | Tags |
|---|---|---|---|
| `ServerTaskLoop.RunOneIterationAsync` | `warp.server_task <Name>` | Internal | `warp.task.name`, `warp.task.lock_held=true|false`, `warp.task.message=<short>` (success), `error.type` (failure) |
| `MutexPipelineBehavior.HandleAsync` (acquire only) | `warp.mutex_acquire` | Internal | `warp.mutex.key`, `warp.mutex.acquired=true|false` |

These do not fit the OTel messaging spec (they're internal coordination, not message-passing). They live under a library-prefixed `warp.*` namespace so they never collide with the standard `messaging.*` keys.

### What this is NOT

- **No companion package.** `Warp.OpenTelemetry` with a `AddWarpInstrumentation()` extension is out of scope. Wiring is `tracerBuilder.AddSource("Warp")` + `meterBuilder.AddMeter("Warp")` — one line in user setup.
- **No new wait-time / queue-depth gauges.** `warp.jobs.enqueued/scheduled/processing` ObservableGauges (DB-backed COUNTs) are useful but require periodic queries we don't want in this iteration.
- **No metric rename to `messaging.client.*`.** Library-prefixed names (`warp.job.*`, `warp.mediator.*`) are stable across the rename pressure of the spec-stable `messaging.client.*` family and easier for operators to filter on a known prefix.
- **No span emission when no `ActivityListener` is attached.** Existing pattern (`StartActivity` returns null with no listener) preserved everywhere — zero overhead when OTel isn't wired.

## Architecture

```
Caller (Activity = "GET /orders", set by ASP.NET)
  │
  ├─ Publisher.Enqueue(SendReport)
  │     ├─ capture Activity.Current.SpanId → local
  │     ├─ Activity "send default" (Producer)             ← NEW (1)
  │     │     tags: messaging.* + warp.job.*
  │     └─ EF: Job row inserted (ParentSpanId = caller's spanId)
  │
  └─ (returns to caller's span)

Worker fetches Job
  Activity "receive default" (Client)                      ← NEW (2)
    tags: messaging.system/operation.name=receive/...
    (closes before consumer span opens)

  Activity "process default" (Consumer, parent = caller)   ← RENAMED (3)
    tags: messaging.* + warp.job.* + warp.job.attempt
    │
    └─ MutexPipelineBehavior
          └─ Activity "warp.mutex_acquire" (Internal)      ← NEW (5)
                tags: warp.mutex.key, warp.mutex.acquired
    └─ Handler.HandleAsync()

App calls IMediator.Send(GetUser(1))
  Activity "process GetUser" (Internal)                    ← NEW (4)
    tags: messaging.system=warp, operation.*=process, destination.name=GetUser, warp.mediator.kind=request
    metric: warp.mediator.in_flight ±1, warp.mediator.duration on close
    │
    └─ IPipelineBehavior chain → IRequestHandler.HandleAsync()

ServerTaskLoop iteration (Heartbeat / Orchestrator / …)
  Activity "warp.server_task Heartbeat" (Internal)         ← NEW (5)
    tags: warp.task.name, warp.task.lock_held
    │
    └─ IServerTask.ExecuteAsync()
```

## Scope Classification

**Substantial feature.** Eight source files modified, ~6 new test files, four batches. Above subagent threshold (3+ batches, 6+ files). One breaking change: consumer span name `Warp.Execute` → `process <queue>` (called out in release notes).

## Change Manifest

### Modified files — Warp.Core

- `src/core/Warp.Core/Logging/WarpTelemetry.cs` — add helpers `StartProducerActivity`, `StartReceiveActivity`, `StartMediatorActivity`, `StartServerTaskActivity`, `StartMutexActivity`. Add instruments `MediatorDuration`, `MediatorInFlight`. Add a small `WarpTelemetryAttributes` static class with the OTel attribute key constants so we never typo `messaging.operation.name`. Update `StartJobActivity` to: (a) take `string queue` and use `"process " + queue` as the span name, (b) set `messaging.operation.type=process`, `messaging.message.conversation_id`, etc.
- `src/core/Warp.Core/Publisher.cs` — wrap `Publish` (line ~75) and `CreateJob` (line ~170) in producer spans. Capture parent span context into local **before** starting the producer span.
- `src/core/Warp.Core/BatchPublisher.cs` — wrap `StartNew` body in a producer span; tag `messaging.batch.message_count = batchChildJobs.Count`.
- `src/core/Warp.Core/Handlers/MediatorDispatcher.cs` — wrap typed request and stream pipelines in Internal-kind activities + `MediatorInFlight ±1` + `MediatorDuration` record. Stream version: capture activity into the iterator and stop it on enumerator dispose/completion via `try/finally` inside `UnwrapStreamTask`.
- `src/core/Warp.Core/Mutex/MutexPipelineBehavior.cs` — wrap the `TryAcquireAsync` call in `warp.mutex_acquire`. Activity stops as soon as the acquire result is known; the handler runs under the parent span unchanged.

### Modified files — Warp.Worker

- `src/core/Warp.Worker/WarpWorkerService.cs` — open `receive <queue>` Client span around the post-fetch bookkeeping (mark ownership, write Processing log) before `StartJobActivity`. After deserializing metadata into `jobContext.Metadata` (line ~138), set `warp.retry.attempt`, `warp.worker.id`, `warp.worker.group` tags on the consumer activity. Add `error.type` tag on the catch path. Remove the now-redundant `Warp.Execute` literal.
- `src/core/Warp.Worker/WarpDispatcherWorker.cs` — same receive-span + retry/worker tags + error.type as `WarpWorkerService`.
- `src/core/Warp.Worker/Services/ServerTaskLoop.cs` — wrap `ExecuteInLockedScopeAsync` inside `RunOneIterationAsync` in `warp.server_task <Name>` Internal span. Stamp `warp.task.lock_held` based on whether the lock was acquired (return value `null` from `ExecuteInLockedScopeAsync` means lock not held / nothing to do).

### Modified test files

- `src/tests/Warp.Tests/Observability/WarpTelemetryTests.cs` — extend with helper-method tests (each `StartXxxActivity` returns null when no listener; correct kind/name when listener attached).
- `src/tests/Warp.Tests/Observability/ActivityTraceTests.cs` — update assertions that match span name `Warp.Execute` → `process <queue>` (search-and-replace in test data).

### New test files

- `src/tests/Warp.Tests/TestData/Helpers/ActivityListenerHarness.cs` — listener helper. `IDisposable Subscribe(string sourceName)` returns a captured-list view. Used by all new test files.
- `src/tests/Warp.Tests/Observability/ProducerSpanTests.cs` — NoDb. Per-publish-method span emission, span name `send <queue>`, kind=Producer, all messaging tags + `warp.job.*` tags, `messaging.batch.message_count` on batch path, `Job.ParentSpanId == caller.SpanId` (not producer's), no spans when no listener attached.
- `src/tests/Warp.Tests/Observability/ReceiveSpanTests.cs` — Integration (PG + SQL): publish a job, wait for completion, assert a `receive <queue>` Client span was emitted before the `process <queue>` Consumer span, both share the trace id, receive is shorter than process.
- `src/tests/Warp.Tests/Observability/MediatorSpanTests.cs` — NoDb. `Mediator.Send` emits `process <RequestType>` Internal; `CreateStream` likewise; activity stays open across `await foreach`; `MediatorDuration` records on close with `status=succeeded|failed|cancelled`; `MediatorInFlight ±1`; exception path sets `Status=Error`, sets `error.type`, records exception.
- `src/tests/Warp.Tests/Observability/MutexSpanTests.cs` — NoDb (uses an in-memory `IWarpLockProvider` test double). Acquired path: `warp.mutex.acquired=true`. Held-by-other path: `acquired=false` and short-circuits with `JobOutcome { State = Deleted }`. Bypass paths (not IJob, no key) emit no mutex span.
- `src/tests/Warp.Tests/Observability/ServerTaskSpanTests.cs` — Integration (PG + SQL): trigger `Heartbeat` once via `WarpTestServer.RunHeartbeatOnceAsync`, assert a `warp.server_task Heartbeat` Internal span with `lock_held=true`. Negative case: a server task that throws records exception + `error.type`.
- `src/tests/Warp.Tests/Observability/RetryAttemptTagTests.cs` — Integration (PG + SQL): publish a job whose handler throws once, wait for completion after retry, assert the retried consumer span carries `warp.job.attempt=2` (first attempt was attempt=1).

### Out of scope (deliberately deferred)

- `Warp.OpenTelemetry` companion package with `AddWarpInstrumentation()` extension.
- ObservableGauges for queue depth (`warp.jobs.enqueued/scheduled/processing` periodic DB COUNTs).
- Wait-time histogram (Enqueued → Processing latency).
- Dead-letter / retried counters (e.g. `warp.job.dead_letter`, `warp.job.retried`) — easy follow-up; would extend the existing `warp.job.*` counter family.
- Pipeline-behavior spans for circuit breaker (out of selected scope; same `warp.<behavior>_<verb>` pattern when added).
- HTTP-server-side span work — already provided by ASP.NET; flows through `Mediator.Send` and picks up the new mediator span automatically.

## Test Manifest

| Test class                | Category | What it covers |
|---------------------------|----------|----------------|
| `ProducerSpanTests`        | NoDb     | Per-publish span, OTel messaging tag set, batch count tag, parent-vs-producer ordering, listener-off ⇒ no overhead |
| `ReceiveSpanTests`         | PG + SQL | `receive <queue>` Client span emitted before `process` Consumer span; same trace id; bounded duration |
| `MediatorSpanTests`        | NoDb     | Send + CreateStream activities, kind/destination tags, in-flight gauge, duration histogram, exception → Error + error.type, cancellation → status=cancelled, stream activity spans the full enumeration |
| `MutexSpanTests`           | NoDb     | Acquired vs held-by-other tag, no-span when not IJob or no key, span exits before next() |
| `ServerTaskSpanTests`      | PG + SQL | `warp.server_task <Name>` per iteration, `lock_held` correct (skip-via-second-server case), exception path records error.type |
| `RetryAttemptTagTests`     | PG + SQL | `warp.job.attempt=N` on each retried consumer span; first attempt =1; absent / =1 on no-retry success |

NoDb tests use `ActivityListenerHarness` to capture spans into a list. Integration tests register an `ActivityListener` before publishing work.

## Implementation Batches

### Batch 1: WarpTelemetry constants + helpers + ActivityListenerHarness

Touches: `WarpTelemetry.cs`, `WarpTelemetryAttributes.cs` (new, in same dir), `WarpTelemetryTests.cs`, `ActivityListenerHarness.cs` (new). Add the tag-key constants, `MediatorDuration`/`MediatorInFlight` instruments, and the five `Start*Activity` helpers. Pure additive — existing `StartJobActivity` keeps working; we add a new overload that takes `queue` and use that in batches 2–4. Tests assert null-when-no-listener, kind-when-listener.

Checkpoint: `dotnet build Warp.slnx`; NoDb suite green.

### Batch 2: Producer spans + consumer-span rename + tag additions

Touches: `Publisher.cs`, `BatchPublisher.cs`, `WarpWorkerService.cs`, `WarpDispatcherWorker.cs`, `ProducerSpanTests.cs` (new), `ActivityTraceTests.cs` (existing — update span-name assertions). Producer spans emit, consumer span name flips to `process <queue>`, new consumer-side tags (`operation.type`, `conversation_id`, `error.type`, `worker.id`, `worker.group`, `batch.message_count` for batch jobs) added.

Checkpoint: NoDb + `ActivityTraceTests` integration green. **Critical assertion**: `ProducerSpanTests` confirms `Job.ParentSpanId == caller.SpanId`, not producer's.

### Batch 3: Receive span + Mediator coverage

Touches: `WarpWorkerService.cs`, `WarpDispatcherWorker.cs`, `MediatorDispatcher.cs`, `ReceiveSpanTests.cs` (new), `MediatorSpanTests.cs` (new). Receive span wraps the post-fetch bookkeeping. Mediator dispatcher wraps typed request/stream pipelines.

Checkpoint: NoDb suite green. Receive span integration test green on at least one provider locally.

### Batch 4: Mutex + retry-tag + server-task spans

Touches: `MutexPipelineBehavior.cs`, `WarpWorkerService.cs` (retry tag), `WarpDispatcherWorker.cs` (retry tag), `ServerTaskLoop.cs`, `MutexSpanTests.cs` (new), `RetryAttemptTagTests.cs` (new), `ServerTaskSpanTests.cs` (new). Wires the remaining three pieces.

Checkpoint: full test suite green on at least one provider locally; `dotnet build Warp.slnx` clean.

## Assumptions

- `ActivitySource.StartActivity` returns null when no listener is attached. Existing `WarpTelemetry.StartJobActivity` already relies on this. Confirmed at `src/core/Warp.Core/Logging/WarpTelemetry.cs:51`.
- `IRetryMetadata.RetriedTimes` is the canonical retry counter; the worker reads it via `MetadataSerializer` (the same path it uses for `JobContext.Metadata` today). No new dependency on `Warp.Core.Retry` from `Warp.Worker`.
- `WarpTestServer.RunHeartbeatOnceAsync` is the right hook for the server-task integration test (referenced in `CLAUDE.md`'s pause/resume description). If unavailable, dispatch via `ServerTaskHost`'s explicit run helper.
- `messaging.operation.type` (the low-cardinality verb) and `messaging.operation.name` (free-form) are both stable in the OTel messaging spec and recognized by current dotnet exporters.

## Risks

- **Stream-activity lifetime.** A leaked open span on the stream path is the obvious foot-gun. Mitigation: explicit unit test asserts the activity is disposed by the time the test finishes; iterator wraps capture in `try/finally`.
- **Consumer-span rename is breaking.** Anyone matching span name `Warp.Execute` in their exporter or alert rules has to change to `process <queue>` (or the messaging tag set, which is the recommended OTel-native filter). Mitigation: release-notes call-out + `Warp.Execute` was added recently and is unlikely to be in production exporter pipelines.
- **Producer-span ordering bug.** If the producer span starts before we read `Activity.Current.SpanId`, the consumer ends up parented to the one-tick producer span. Mitigation: explicit local-variable capture order in `Publisher.cs` and `BatchPublisher.cs`, asserted by `ProducerSpanTests`.
- **Server-task `lock_held=false` ambiguity.** A server task that returns null because it had nothing to do looks identical to one that lost the lock race. We use the `_lockKey != null` plus the `IAsyncDisposable? handle == null` signal inside `ExecuteInLockedScopeAsync` to set the tag accurately.
- **Mediator span on non-`IJob` requests with `MutexPipelineBehavior` registered globally.** The behavior short-circuits for non-`IJob`. The mutex span must short-circuit at the same place — start the activity *after* the `request is not IJob` and `ConcurrencyKey == null` early returns. Tested in `MutexSpanTests`.
- **Hot-path overhead at publish.** Each `Publisher.Enqueue` gains one allocation for the producer Activity when a listener is registered. Zero overhead when no listener (the helper returns null and `?.SetTag` calls compile to null-checks).

## Public Contracts Added

- `WarpTelemetry.MediatorDuration` (Histogram&lt;double&gt;, `warp.mediator.duration`, ms)
- `WarpTelemetry.MediatorInFlight` (UpDownCounter&lt;long&gt;, `warp.mediator.in_flight`)
- `WarpTelemetry.StartProducerActivity(string queue, string operation, JobKindForTagging kind)` returning `Activity?`
- `WarpTelemetry.StartReceiveActivity(string queue)` returning `Activity?`
- `WarpTelemetry.StartMediatorActivity(string requestTypeName, string responseTypeName, string kind)` returning `Activity?`
- `WarpTelemetry.StartServerTaskActivity(string taskName)` returning `Activity?`
- `WarpTelemetry.StartMutexActivity()` returning `Activity?` (caller stamps the key/result tags)
- `WarpTelemetry.StartJobActivity(Guid traceId, string? parentSpanId, string queue)` — new overload; existing `StartJobActivity(Guid, string?)` kept as a thin shim for back-compat (in-batch-2: callers updated to the new overload)
- `WarpTelemetryAttributes` — public static class of OTel attribute key constants

All additive except for the consumer span name change (`Warp.Execute` → `process <queue>`) and the addition of `messaging.operation.type` alongside the existing `.name` (additive).

## Security Impact

None. New tags carry queue names, type names, mutex keys, server-task names, request type names, worker ids — same shape of information already on the existing consumer span. Mutex keys may be user-chosen and could in theory contain PII (`customer-{email}`); that's a documentation issue identical to the existing `IMutexMetadata` risk. Producer / receive / process spans do not log payloads (§1.2). Histograms record durations and queue/type/status only — no PII.
