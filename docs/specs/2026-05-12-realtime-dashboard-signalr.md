# Spec: Realtime dashboard push via SignalR

## Problem

The Warp dashboard polls the REST API on intervals to feel "live": `DashboardPage` calls `getStatsHistory` every 60s, `MainLayout` polls navbar stats via `usePolling`, `FilteredJobsTable` refetches every 2s, `ConcurrencyLimitsPage` / `CountersPage` every 5s, `DetailPage` uses `usePolling` for job logs and child counts. Polling is wasteful (most ticks return identical data), latency is bounded below by the interval (a job that completes at t=0 isn't visible until t≤2s on the job list), and at any non-trivial fleet size the dashboard generates a constant baseline load on the API and DB that scales with the number of connected clients, not the actual rate of change.

The backend already has a push backbone for cross-server fanout. `IWarpNotificationTransport` (Postgres LISTEN/NOTIFY, SQL Server Service Broker) is opted-in via `opt.UseDatabasePush()` and is consumed by `NotificationListenerTask<TContext>`, which fires the in-process `ServerTaskSignals<TContext>.SignalJobFinalized` / `SignalMessageEnqueued` on each remote notification. The Orchestrator and MessageRouter background tasks subscribe to those signals and wake up on push. Adding a dashboard consumer is a strict superset of what's already wired: subscribe to the same signal surface, fan out to connected dashboard clients via a SignalR hub. The dashboard inherits the multi-server fanout for free.

This work adds an opt-in addon `opt.AddDashboardPush()` that registers a SignalR hub plus an in-process broadcaster. The frontend probes a `/api/dashboard/push/probe` endpoint and either upgrades to SignalR (when present) or stays on the current polling (when not). Push is best-effort: clients always reconcile via a 30s safety-net refetch and a refetch-on-reconnect, so the REST endpoints remain the source of truth and the hub is purely an invalidation channel.

Reference designs studied: TickerQ's SignalR live dashboard, Hangfire's `/hangfire/stats` SSE poll (lighter than SignalR — but no transport negotiation, no auth integration, no per-page groups for the future), and the existing `AddConcurrency` / `UseDatabasePush` addon shape in this repo.

## Solution (v1)

### One opt-in addon, two registration entry points

`opt.AddDashboardPush()` extension on `IWarpBuilder<TContext>` — same shape as `AddRetry()`, `AddConcurrency()`, `AddCircuitBreaker()`. Adding the call registers (a) the `WarpDashboardHub`, (b) the `DashboardBroadcaster` hosted service that subscribes to `ServerTaskSignals<TContext>` and broadcasts to connected clients, (c) the `IDashboardPushOptions` snapshot used by the probe endpoint. Without `AddDashboardPush()` the hub is not mapped, the probe endpoint returns 404, and the frontend falls back to polling — identical hide-on-404 pattern to `/api/concurrency` (§8.6).

The hub is mounted under `${RoutePrefix}/api/hub` (e.g., `/warp/api/hub`) so that the existing `WarpUIMiddleware` 401-for-API-paths behavior applies to negotiate and to long-poll/SSE/WS frames. The `IWarpAuthorizationFilter` registered on `WarpUIOptions` runs against every hub-bound HTTP request — no parallel auth code path.

### Backend topology

```
Worker / NotificationListenerTask  ──signals──▶  ServerTaskSignals<TContext>
                                                          │
                                                          ▼
                                                 DashboardBroadcaster        (subscribes JobFinalized + MessageEnqueued)
                                                          │
                                                          ▼ (debounced)
                                                 IHubContext<WarpDashboardHub>.Clients.All.SendAsync(...)
                                                          │
                                                          ▼
                                                 Connected dashboard clients
```

The broadcaster never queries the database. It receives an invalidation signal and emits a typed event (`{ kind: "JobFinalized" }`, `{ kind: "MessageEnqueued" }`). Clients refetch from the existing REST endpoints to get authoritative state. This keeps the hub schema tiny (no PII risk, §1.2 — payloads carry only `kind`), keeps the worker hot path untouched (§2.2 / §6.1 — broadcaster runs out-of-band on signals, not in the worker fetch/execute loop), and lets us extend the event vocabulary later without changing wire-level guarantees.

A **coalescing window** (default 100ms) collapses bursts: if 50 jobs finalize in a 100ms window, the broadcaster emits one `JobFinalized` invalidation, not 50. Without this, a batch completion would push 50× per-client `SendAsync` calls and trigger 50 client-side refetches in a thundering-herd pattern. The window is configurable via `WarpDashboardPushConfiguration.CoalesceWindow`; setting it to `TimeSpan.Zero` disables coalescing.

### Multi-server fanout

For free, via the existing DB push transport. When `opt.UseDatabasePush()` is also configured: server A finalizes a job → server A's worker calls `ServerTaskSignals<TContext>.SignalJobFinalized()` and the local DB push transport emits a `JobFinalized` notification → all subscribed `NotificationListenerTask` instances (servers A and B) fire their local `SignalJobFinalized` → both servers' `DashboardBroadcaster` instances emit to their connected dashboard clients. No new transport, no Redis backplane.

Without `UseDatabasePush()`: each server's broadcaster only emits events sourced from its own workers and orchestrator. A dashboard client connected to server A sees server-A events; events from server B don't reach it until the 30s safety-net refetch. This is documented as a known limitation. (Adding `Microsoft.AspNetCore.SignalR.StackExchangeRedis` is the standard escape hatch and remains a v2 option — explicitly out of scope here.)

### Auth model

`WarpDashboardHub.OnConnectedAsync` calls `IWarpAuthorizationFilter.Authorize(Context.GetHttpContext())` and aborts the connection on `false`. This is enforced in three places:
1. The HTTP negotiate request hits `WarpUIMiddleware`'s existing path-based auth (returns 401 because the path contains `/api/`, mirroring all other API endpoints).
2. The hub itself re-checks on `OnConnectedAsync` — defence in depth and the only auth surface for transports that skip negotiate (pure WebSocket).
3. The cookie filter (`CookieAuthorizationFilter` private to `WarpUIMiddleware`) works unchanged — SignalR's `IHttpContextAccessor` gives the hub the same `HttpContext` the cookie filter validates.

When `Authorization` is null (no auth configured, default), the hub accepts all connections — consistent with the rest of Warp.UI when no filter is set.

### Public API

```csharp
// Registration
services.AddWarp<MyContext>(opt =>
{
    opt.UseDatabasePush();      // for cross-server fanout (optional but recommended)
    opt.AddDashboardPush();     // registers hub + broadcaster
});

// Optional configuration
services.AddWarp<MyContext>(opt =>
{
    opt.AddDashboardPush(cfg =>
    {
        cfg.CoalesceWindow = TimeSpan.FromMilliseconds(250);
    });
});
```

```csharp
// Public surface added
namespace Warp.UI.DashboardPush;

public sealed class WarpDashboardPushConfiguration
{
    public TimeSpan CoalesceWindow { get; set; } = TimeSpan.FromMilliseconds(100);
}

public static class DashboardPushServiceConfiguration
{
    public static IWarpBuilder<TContext> AddDashboardPush<TContext>(
        this IWarpBuilder<TContext> builder,
        Action<WarpDashboardPushConfiguration>? configure = null)
        where TContext : DbContext;
}

// The hub itself is internal — clients hit it by URL, not type. Event names ("JobFinalized", "MessageEnqueued") are the public wire contract.
```

Wire contract (SignalR hub methods invoked on clients):

| Server → client method | Payload | Trigger |
|---|---|---|
| `JobFinalized` | `{}` (empty) | Any job reaches a terminal state |
| `MessageEnqueued` | `{}` (empty) | A `Kind=Message` job is published |

Clients treat both as "refetch whatever you're currently showing." Future events (per-job state changes, log-line appended) extend the vocabulary; v1 stops at these two because they cover the polling surface.

### Frontend wiring

- Add `@microsoft/signalr` dependency to `src/ui/package.json`.
- New `src/ui/src/stores/realtime.ts` — Zustand slice holding `{ status: 'connecting' | 'connected' | 'disconnected' | 'disabled', lastEventAt: number | null }`. Owns the `HubConnection` instance and reconnect lifecycle.
- New `src/ui/src/hooks/useRealtimeRefetch.ts` — subscribes a refetch callback to a specified event (`JobFinalized` / `MessageEnqueued`) with a 30s safety-net `setInterval` fallback. Replaces direct `usePolling` calls on push-aware pages.
- On app boot: probe `GET /api/dashboard/push/probe`. On 200, attempt the SignalR connection. On 404 (push not registered) or connection failure (auth, network), leave status `disabled` and pages keep using `usePolling` at current intervals.
- Reconnect strategy: SignalR's built-in `withAutomaticReconnect()` plus an `onreconnected` handler that fires `JobFinalized` once locally (drain-on-reconnect equivalent of `NotificationListenerTask.DrainSignals`).
- Pages converted to event + safety-net refetch in v1: `DashboardPage` (stats), `MainLayout` (navbar stats), `JobListPage` + `FilteredJobsTable` (job lists), `DetailPage` (job logs / counts), `CountersPage`, `ConcurrencyLimitsPage`. The `usePolling` hook stays — it's the fallback path.

A small connection-status indicator goes in the navbar (green dot when connected, gray when disabled, amber when reconnecting). One DOM element, no settings UI.

### Telemetry

Two new counters on `WarpTelemetry`:
- `warp.dashboard.events.broadcast` — `Counter<long>`, `unit: "{event}"`, incremented per hub broadcast (post-coalesce, so 50-job batch → 1).
- `warp.dashboard.connections.active` — `UpDownCounter<long>`, `unit: "{connection}"`, tracks live hub connections.

The OTel messaging conventions already in `WarpTelemetry` don't apply here — these are dashboard transport metrics, not messaging spans.

## Scope

- **Classification:** substantial-feature (new addon, new wire surface, multi-project change spanning Warp.UI, Warp.Core, frontend, tests, docs).
- **Security impact:** low. New auth surface (hub) reuses `IWarpAuthorizationFilter`; no new credential handling. Payloads carry no PII (event-only, refetch-driven). Negotiate goes through the same middleware that already gates `/api/`.
- **Breaking changes:** none. Pure addition. Without `opt.AddDashboardPush()`, behavior is identical to today.

## Implementation batches

### Batch 1 — Backend: hub, broadcaster, addon registration

Files:
- `src/core/Warp.UI/DashboardPush/WarpDashboardPushConfiguration.cs` (new)
- `src/core/Warp.UI/DashboardPush/WarpDashboardHub.cs` (new)
- `src/core/Warp.UI/DashboardPush/DashboardBroadcaster.cs` (new)
- `src/core/Warp.UI/DashboardPush/DashboardPushServiceConfiguration.cs` (new — `AddDashboardPush()` extension)
- `src/core/Warp.UI/DashboardPush/IDashboardPushMarker.cs` (new — marker registered iff `AddDashboardPush()` was called; used by probe endpoint)
- `src/core/Warp.UI/Warp.UI.csproj` — add `<ProjectReference Include="..\Warp.Worker\Warp.Worker.csproj" />` for `ServerTaskSignals<TContext>` access; SignalR ships via `Microsoft.AspNetCore.App` framework reference so no NuGet add
- `src/core/Warp.UI/Endpoints/WarpEndpoints.cs` — add `MapGet("dashboard/push/probe", ...)` returning 200 iff `IDashboardPushMarker` is registered; otherwise 404
- `src/core/Warp.UI/UIMiddleware/WarpUIBuilder.cs` — add `MapHub<WarpDashboardHub>($"{options.RoutePrefix}/api/hub")` when the marker is registered
- `src/core/Warp.Core/Logging/WarpTelemetry.cs` — add `DashboardEventsBroadcast` counter and `DashboardConnectionsActive` up/down counter

Checkpoint: `dotnet build src/Warp.slnx` clean. No new test failures.

### Batch 2 — Backend tests

Files:
- `src/tests/Warp.Tests/DashboardPush/DashboardBroadcasterTests.cs` (new — abstract base + `[GenerateDatabaseTests]`)
  - signals fan out: `SignalJobFinalized` → one `IHubContext.Clients.All.SendAsync("JobFinalized", …)` call
  - coalescing: 50 signals in `<CoalesceWindow` → 1 broadcast; verify via fake `IHubContext`
  - `CoalesceWindow = TimeSpan.Zero` disables coalescing
- `src/tests/Warp.Tests/DashboardPush/DashboardPushIntegrationTests.cs` (new — `IntegrationTestBase`)
  - publish a job through `WarpTestServer`, await `WaitForCompletion`, assert a hub broadcast was observed (fake `IHubContext` captured into a `ConcurrentQueue`)
  - dual-server variant: two `WarpTestServer` instances on the same DB with `UseDatabasePush()` — server A finalizes, server B's broadcaster fires
- `src/tests/Warp.Tests/DashboardPush/DashboardPushAuthTests.cs` (new — uses ASP.NET `WebApplicationFactory` against the UI middleware)
  - unauthenticated negotiate request → 401
  - authenticated negotiate → 200; subsequent hub connection accepted; `OnConnectedAsync` rejects with no auth context only when filter says so

Checkpoint: `dotnet test --project tests/Warp.Tests/Warp.Tests.csproj -- --filter-namespace "Warp.Tests.DashboardPush"` green on both PG and SQL Server.

### Batch 3 — Frontend: signalr client, store, hook, probe

Files:
- `src/ui/package.json` — add `@microsoft/signalr`
- `src/ui/src/stores/realtime.ts` (new)
- `src/ui/src/hooks/useRealtimeRefetch.ts` (new)
- `src/ui/src/api/realtime.ts` (new — probe call + hub URL helper)
- `src/ui/src/main.tsx` — boot-time probe + connection attempt
- `src/ui/src/layouts/MainLayout.tsx` — add connection-status indicator; keep existing `usePolling` as fallback

Checkpoint: `npm run build` clean, `npm run lint` clean.

### Batch 4 — Frontend page conversions

Files:
- `src/ui/src/pages/dashboard/DashboardPage.tsx` — `useRealtimeRefetch('JobFinalized', refetch, 30_000)` for stats refresh; keep 60s historical poll (out of push scope — hourly data)
- `src/ui/src/pages/jobs/JobListPage.tsx` — refetch on `JobFinalized` for current state filter
- `src/ui/src/components/FilteredJobsTable.tsx` — drop 2s polling, switch to event + 30s safety
- `src/ui/src/pages/detail/DetailPage.tsx` — refetch logs + counts on `JobFinalized` (filtered by current jobId via React state — not server-side, see "Out of scope")
- `src/ui/src/pages/counters/CountersPage.tsx` — event + 30s safety (was 5s)
- `src/ui/src/pages/concurrency/ConcurrencyLimitsPage.tsx` — same
- `src/ui/src/pages/messages/MessagesPage.tsx` — refetch on `MessageEnqueued`

Checkpoint: `npm run build` clean. Manual: open dashboard against the demo app, observe stats updating without polling traffic in the Network tab (push events visible on the hub).

### Batch 5 — Docs

Files:
- `README.md` — new "Realtime dashboard push" section near the DB Push section; explicit "single-server only without `UseDatabasePush`"
- `CLAUDE.md` — append §2.10 documenting the addon, its interaction with §2.9, and §8.7 reference to the broadcaster as another `ServerTaskSignals` consumer

Checkpoint: `dotnet build` (it runs the SPA build via the csproj target) + full test suite green.

## Test manifest

| Test | Layer | Why |
|---|---|---|
| `DashboardBroadcasterTests.SignalJobFinalized_FiresHubBroadcast_Once` | unit | signal → broadcast wiring |
| `DashboardBroadcasterTests.CoalesceWindow_Bursts_CollapseToOneBroadcast` | unit | debounce correctness |
| `DashboardBroadcasterTests.ZeroWindow_DisablesCoalescing` | unit | escape hatch |
| `DashboardBroadcasterTests.MessageEnqueuedSignal_FiresMessageEnqueuedEvent` | unit | second event surface |
| `DashboardPushIntegrationTests.JobCompletion_FiresBroadcast_OnSameServer` | integration | end-to-end signal path with real worker |
| `DashboardPushIntegrationTests.JobCompletion_OnServerA_FiresBroadcast_OnServerB` | integration, dual-server, DB push on | the multi-server claim |
| `DashboardPushIntegrationTests.WithoutDatabasePush_OnlyLocalServerBroadcasts` | integration, dual-server, DB push off | document the limitation |
| `DashboardPushAuthTests.UnauthenticatedNegotiate_Returns401` | http | auth integration |
| `DashboardPushAuthTests.AuthenticatedNegotiate_Returns200` | http | auth happy path |
| `DashboardPushAuthTests.HubOnConnected_RejectsWhenFilterDenies` | http | defence-in-depth |
| `WarpEndpointsTests.PushProbe_Returns404_WhenAddonNotRegistered` | unit | hide-on-404 contract |
| `WarpEndpointsTests.PushProbe_Returns200_WhenAddonRegistered` | unit | probe contract |

Frontend tests: none in v1. The UI codebase ships with Playwright (`src/ui/playwright.config.ts`) but no current convention for unit-testing Zustand stores. A Playwright smoke that loads the dashboard against the demo app and verifies stats update after a published job is the right v2 addition; punting now to keep batch count bounded.

## Assumptions

- `Warp.UI` may take a `ProjectReference` on `Warp.Worker`. There is no existing dep but no rule forbidding it (CLAUDE.md describes Warp.UI as the dashboard package; Warp.Worker as the runtime). The dashboard cannot do anything useful without the worker host being present in the same process anyway.
- SignalR is provided by `Microsoft.AspNetCore.App` framework reference — no NuGet add to `Warp.UI.csproj`. Confirmed by inspection: Warp.UI already declares `<FrameworkReference Include="Microsoft.AspNetCore.App" />`.
- `IHubContext<WarpDashboardHub>` is fakeable in tests by registering a substitute implementation in DI — standard ASP.NET Core SignalR test pattern. No special test infra needed.
- The CLAUDE.md description of `FixtureKind.Integration` / `FixtureKind.MultiServer` does not match the actual `DatabaseTestsGenerator` (which only knows `WithPush: bool`). Tests will use `[GenerateDatabaseTests]` (no kind) and instantiate `WarpTestServer` directly in `IntegrationTestBase`-derived classes — consistent with how `WarpTestServer.cs` is actually used in this repo.
- The user runs `opt.UseDatabasePush()` for multi-server deployments. Documentation will say so prominently.

## Risks

- **Auth integration**: the riskiest piece. If `OnConnectedAsync` doesn't see the cookie because of SignalR's negotiate→WebSocket handoff, unauth clients could hold connections. Mitigation: explicit `DashboardPushAuthTests.HubOnConnected_RejectsWhenFilterDenies` test against `WebApplicationFactory` with realistic headers, plus the middleware-level 401 on negotiate as the first defence.
- **Reconnect storms after deploy**: 100 reconnecting clients drain on reconnect → 100 simultaneous refetches. Mitigation: SignalR's built-in random backoff for reconnect; server side, the broadcaster is rate-limited by the coalesce window (a reconnect doesn't itself emit anything — the *client* refetches REST).
- **Coalescing latency vs. responsiveness**: 100ms is short enough to feel instant, long enough to collapse batches. Verified by `DashboardBroadcasterTests`. Configurable per-deployment.
- **Per-job page scoping**: `DetailPage` listens to `JobFinalized` and refetches if the event "might" be about its current job. Without per-job groups (out of scope), every detail page refetches on every finalize. At normal dashboard usage (<10 detail pages open across a team) this is cheap. At pathological usage it would be wasteful. Documented as a v2 candidate (`Clients.Group($"job:{id}")`).

## Out of scope (v1)

- Per-user / per-job SignalR groups (`Clients.User`, `Clients.Group`). Documented as a v2 candidate for `DetailPage` scoping.
- Client-initiated hub commands (`hubConnection.invoke('RequeueJob', id)`). The hub is broadcast-only. REST stays the command surface.
- Redis backplane (`Microsoft.AspNetCore.SignalR.StackExchangeRedis`). DB push is the backplane.
- Removing polling entirely. The 30s safety-net poll and the 60s historical-chart poll on `DashboardPage` remain — they're the correctness floor.
- A user-facing toggle ("force polling"). The probe + 404 fallback is sufficient.
- Push payload data (e.g., emitting the actual job DTO on `JobFinalized`). Events are invalidations; clients refetch.
- Frontend unit tests / Playwright smoke. v2.
- Compression on hub frames. Empty payloads ⇒ irrelevant.
- A new dashboard nav entry. The status indicator in `MainLayout` is the only visible UI surface.
