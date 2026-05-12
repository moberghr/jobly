# Plan: Realtime dashboard push via SignalR

Spec: `docs/specs/2026-05-12-realtime-dashboard-signalr.md`. Implementation is broken into 5 batches that each compile + test cleanly. The backend batches (1, 2) and the docs batch (5) can be done independently of the frontend (3, 4); 4 depends on 3 (which adds the SignalR client + store).

## Batch 1 — Backend: hub, broadcaster, addon registration

**Goal:** the SignalR hub exists, is registered when `AddDashboardPush()` is called, is reachable at `${RoutePrefix}/api/hub`, and a probe endpoint returns 200/404 for hide-on-404 fallback. No tests yet; just shape the surface.

**Why this slice:** matches the existing addon shape (`AddRetry`, `AddConcurrency`, `AddCircuitBreaker`) so it's the smallest checkpoint that the public API doesn't regress and the build stays green. Tests come in batch 2 once the surface is locked.

### Steps

1. **Decide on the Warp.UI → Warp.Worker dep.** Add `<ProjectReference Include="..\Warp.Worker\Warp.Worker.csproj" />` to `Warp.UI.csproj`. This unblocks `ServerTaskSignals<TContext>` from the broadcaster. There is no opposite-direction dep that would create a cycle. (Verified: Warp.Worker currently references only Warp.Core and the provider packages.)

2. **`WarpDashboardPushConfiguration`** (Warp.UI/DashboardPush/). Single property `CoalesceWindow` (default 100ms). Follows `WarpDatabasePushConfiguration` shape.

3. **`IDashboardPushMarker`** + empty `DashboardPushMarker` impl. Registered iff `AddDashboardPush()` is called. Used by the probe endpoint and by `UseWarpUI` to decide whether to `MapHub`. Same hide-on-404 idiom as `IConcurrencyLimitManager?` in `WarpEndpoints.MapWarpApiEndpoints`.

4. **`WarpDashboardHub`** extends `Microsoft.AspNetCore.SignalR.Hub`. Override `OnConnectedAsync`: increment `WarpTelemetry.DashboardConnectionsActive`; resolve `IWarpAuthorizationFilter?` and abort connection if filter exists and denies the connection's `HttpContext`. Override `OnDisconnectedAsync` to decrement the counter. No client-callable methods (broadcast-only).

5. **`DashboardBroadcaster<TContext>`** as `BackgroundService`. Constructor takes `ServerTaskSignals<TContext>`, `IHubContext<WarpDashboardHub>`, `WarpDashboardPushConfiguration`, `TimeProvider`, `ILogger<>`. On `ExecuteAsync`, subscribes to `ServerTaskSignal.JobFinalized` and `ServerTaskSignal.MessageEnqueued` via `Subscribe(channel, wake)`. Each signal sets a `Channel<NotificationKind>` write or just an `Interlocked.Exchange` flag per channel. A single `await Task.Delay(CoalesceWindow, ct)` loop reads the latched flags and emits at most one event per kind per window. Disposes the `Subscription` handles on stop. Increments `WarpTelemetry.DashboardEventsBroadcast` per broadcast.

   Concurrency note: `ServerTaskSignals.Subscribe` returns an internal `Subscription` type. Access pattern: `_signals.Subscribe(ServerTaskSignal.JobFinalized, () => _jobFinalizedPending = 1)`. The `internal` accessibility of `Subscription` means broadcaster must live in the same assembly as `ServerTaskSignals`, OR `Subscribe` needs to be public, OR `InternalsVisibleTo` is added. Per memory rule "Retry uses only public API — Addons like Retry must use only Core's public API, no InternalsVisibleTo", we promote `Subscribe`'s return type to `IDisposable` and make the method public. The `Subscription` class stays internal.

6. **`DashboardPushServiceConfiguration.AddDashboardPush()`** extension. Registers `WarpDashboardPushConfiguration` (with optional configure lambda), `IDashboardPushMarker` → `DashboardPushMarker`, `DashboardBroadcaster<TContext>` as `IHostedService`, and `services.AddSignalR()` (idempotent — SignalR's own DI is safe to register twice).

7. **`WarpUIBuilder.UseWarpUI`** — after `app.UseMiddleware<WarpUIMiddleware>(options)`, check `app.Services.GetService<IDashboardPushMarker>()`. If non-null, `app.MapHub<WarpDashboardHub>($"{options.RoutePrefix}/api/hub")`. No options, no policy — auth flows through the middleware + `OnConnectedAsync`.

8. **`WarpEndpoints.MapWarpApiEndpoints`** — add `apiGroup.MapGet("dashboard/push/probe", ([FromServices] IDashboardPushMarker? marker) => marker is null ? Results.NotFound() : Results.Ok(new { enabled = true }))`. Same hide-on-404 shape as `concurrency`.

9. **`WarpTelemetry`** — add the two counters. Follow existing naming and unit conventions (`warp.dashboard.events.broadcast`, `warp.dashboard.connections.active`).

10. **`ServerTaskSignals<TContext>`** — change `Subscribe` from `internal` to `public`, return type from `Subscription` to `IDisposable`. `Subscription` stays internal. No callers break (existing callers are inside Warp.Worker; they keep working with the upcasted return type).

**Checkpoint:** `dotnet build src/Warp.slnx` green. No new test failures (no new tests yet). Manually verify: a sample app calling `services.AddWarp<X>(opt => opt.AddDashboardPush())` followed by `app.UseWarpUI()` registers the hub (visible in `app.MapEndpoints` enumeration).

## Batch 2 — Backend tests

**Goal:** prove the broadcaster wiring, coalescing, multi-server fanout, and auth integration.

### Steps

1. **`FakeHubContext`** test helper that implements `IHubContext<WarpDashboardHub>` and captures every `Clients.All.SendAsync(method, args)` call into a `ConcurrentQueue<(string Method, object?[] Args)>`. Reusable across all dashboard-push tests. Lives in `Warp.Tests/DashboardPush/`.

2. **`DashboardBroadcasterTests : IAsyncLifetime`** abstract base with `[GenerateDatabaseTests]`. Each test news a real `ServerTaskSignals<TestContext>`, a `FakeHubContext`, a `WarpDashboardPushConfiguration { CoalesceWindow = ... }`, and a `DashboardBroadcaster<TestContext>`. Test names:
   - `SignalJobFinalized_FiresHubBroadcast_Once` — one signal, `await Task.Delay(CoalesceWindow * 2)`, fake context has exactly one `("JobFinalized", _)` entry.
   - `CoalesceWindow_Bursts_CollapseToOneBroadcast` — fire 50 signals back-to-back inside the window, observe one entry.
   - `ZeroWindow_DisablesCoalescing` — `CoalesceWindow = TimeSpan.Zero`, 5 sequential signals → 5 entries.
   - `MessageEnqueuedSignal_FiresMessageEnqueuedEvent` — second event surface.
   - `DisposingBroadcaster_UnregistersSignalSubscriptions` — start, dispose, fire signal, assert no broadcast.

   Why `[GenerateDatabaseTests]` if there's no DB? `ServerTaskSignals<TContext>` is generic over `TContext : DbContext`. The fixture provides `TestContext`. Tests don't touch the DB but inherit the constructor pattern. Could probably be a `[NoDb]` xUnit trait; we'll pick `[GenerateDatabaseTests]` for consistency and accept the cost.

3. **`DashboardPushIntegrationTests : IntegrationTestBase`** with `[GenerateDatabaseTests]`. Uses `WarpTestServer.StartAsync(_fixture, configure: cfg => { /* call AddDashboardPush + inject FakeHubContext */ })`. Tests:
   - `JobCompletion_FiresBroadcast_OnSameServer` — publish a fast-completing job, `WaitForCompletion`, observe a `("JobFinalized", _)` entry on the captured fake context.
   - `JobCompletion_OnServerA_FiresBroadcast_OnServerB` — two `WarpTestServer` instances on the same DB, both with `UseDatabasePush()` + `AddDashboardPush()`. Publish via server A; both fake contexts (one per server) observe the broadcast. Verifies the multi-server claim.
   - `WithoutDatabasePush_OnlyLocalServerBroadcasts` — two servers, neither calls `UseDatabasePush`. Publish via A; only A's fake context observes. Documents the limitation as an executable test.

   The `configure` parameter to `WarpTestServer.StartAsync` is the documented way to swap services per-test (per CLAUDE.md §"Tests"). For the fake hub context, register `Services.AddSingleton(FakeHubContext)` then `Services.AddSingleton<IHubContext<WarpDashboardHub>>(sp => sp.GetRequiredService<FakeHubContext>())`.

4. **`DashboardPushAuthTests`** — uses `Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<TStartup>` (already a pattern in this repo, used by `DashboardAuthTests` for the cookie-login flow). Tests:
   - `UnauthenticatedNegotiate_Returns401` — POST `/warp/api/hub/negotiate?negotiateVersion=1` with no cookie → 401.
   - `AuthenticatedNegotiate_Returns200` — same with the cookie set → 200.
   - `HubOnConnected_RejectsWhenFilterDenies` — register a filter that returns false; POST negotiate succeeds (middleware path), but a real `HubConnectionBuilder` connection rejects with `HubException` from `OnConnectedAsync`. (This proves the defense-in-depth check; even if negotiate is mis-configured to allow, the hub itself denies.)

5. **`WarpEndpointsPushProbeTests`** (NoDb). Two tests: probe with `IDashboardPushMarker` registered → 200 with `{ enabled: true }`. Probe with no marker → 404.

**Checkpoint:** `dotnet test --project src/tests/Warp.Tests/Warp.Tests.csproj -- --filter-namespace "Warp.Tests.DashboardPush"` green on both PG and SQL Server. Full test suite still under ~1m 30s.

## Batch 3 — Frontend: signalr client, store, probe

**Goal:** boot-time probe, connection lifecycle, status indicator. No page conversions yet — those land in batch 4 so each frontend batch ships independently testable.

### Steps

1. **Add `@microsoft/signalr` to `src/ui/package.json`** (latest stable that matches React 19 / Vite 8 — `^9.0.0` series is current). Run `npm install` to refresh `package-lock.json`. The csproj `InstallUI` target handles this on next `dotnet build`, but committing the lock keeps reproducibility.

2. **`src/ui/src/stores/realtime.ts`** — Zustand slice. State: `{ status: 'idle' | 'probing' | 'connecting' | 'connected' | 'disconnected' | 'disabled', lastEventAt: number | null, connection: HubConnection | null }`. Actions: `probeAndConnect()`, `disconnect()`. The store *owns* the SignalR connection lifecycle; pages never touch the HubConnection directly.

   `probeAndConnect()`:
   - status = 'probing'; `await api.get('/dashboard/push/probe')`.
   - on 404 or non-2xx → status = 'disabled'; return.
   - on 2xx → status = 'connecting'; build `HubConnection` with `.withUrl(\`\${apiPath}hub\`, { withCredentials: true }).withAutomaticReconnect()`. Wire `onclose`, `onreconnecting`, `onreconnected` handlers that update status + lastEventAt + fire a synthetic refetch event for the drain-on-reconnect equivalent.
   - on `start()` resolve → status = 'connected'; on reject → status = 'disabled' (404 path swallowed, real failure logged).

3. **`src/ui/src/hooks/useRealtimeRefetch.ts`** — `useRealtimeRefetch(event: 'JobFinalized' | 'MessageEnqueued', refetch: () => void, safetyMs: number = 30_000)`:
   - subscribes to the hub event via `connection.on(event, refetch)` when status = 'connected'.
   - sets a `setInterval(refetch, safetyMs)` regardless of connection state.
   - on `connection` change, unsubscribes the previous binding and re-subscribes.
   - on `onreconnected` (observed via store), calls `refetch()` once.
   - returns nothing — fire-and-forget like `usePolling`.

4. **`src/ui/src/api/realtime.ts`** — `probeDashboardPush(): Promise<boolean>` (true on 200, false on 404/error). Plus a `getHubUrl()` helper that resolves `${apiPath}hub` from `config.apiPath`.

5. **`src/ui/src/main.tsx`** — on app boot (or first render of `MainLayout`), call `useRealtimeStore.getState().probeAndConnect()`. Once, idempotent.

6. **`src/ui/src/layouts/MainLayout.tsx`** — small status indicator near the existing theme toggle / logout button. A single `<span>` with a colored dot:
   - `connected` → green
   - `connecting` / `reconnecting` → amber pulse (re-use `animate-pulse` from tailwind)
   - `disabled` → gray (hidden by default, visible only in dev for diagnostics — wrap in `import.meta.env.DEV` check to keep the UX clean)
   - `disconnected` → red briefly, then transitions to `connecting`

**Checkpoint:** `npm run build` clean. `npm run lint` clean. Boot the demo app, open the dashboard with `AddDashboardPush()` enabled — status indicator green. Disable the addon and reload — indicator absent (or grey in dev). No page conversions yet so pages still poll.

## Batch 4 — Frontend page conversions

**Goal:** replace polling with `useRealtimeRefetch` on every page where the hub event maps cleanly. Keep `usePolling` as the fallback.

### Steps

For each page in the list:
1. **Identify the existing polling site** (either `usePolling(callback, ms)` or `useEffect` with `setInterval`).
2. **Replace with `useRealtimeRefetch(event, callback, 30_000)`** for the matching event. The 30s safety-net poll inside the hook covers the "no push" path.
3. **For pages with multiple distinct fetches** (e.g., `DetailPage` fetches job + counts + logs at different intervals), subscribe each refetch separately. They share the same hub connection.

Per-page event mapping:

| Page | Current polling | Event |
|---|---|---|
| `DashboardPage` (stats) | `useDashboardStore.fetchStats` polled via `MainLayout` | `JobFinalized` |
| `DashboardPage` (history) | `setInterval(getStatsHistory, 60_000)` | leave — hourly aggregate, push doesn't help |
| `MainLayout` (navbar stats) | `usePolling(...stats..., 2000)` | `JobFinalized` |
| `JobListPage` / `FilteredJobsTable` | `setInterval(update, 2000)` | `JobFinalized` |
| `DetailPage` | `usePolling` for detail + logs | `JobFinalized` (filter by current jobId client-side after refetch) |
| `CountersPage` | `setInterval(fetchAll, 5000)` | `JobFinalized` |
| `ConcurrencyLimitsPage` | `setInterval(fetchAll, 5000)` | leave — admin-managed, no signal source |
| `MessagesPage` | (likely `usePolling`) | `MessageEnqueued` + `JobFinalized` |

**Checkpoint:** `npm run build` clean. Manual smoke (Vite dev server against the demo app):
- Open dashboard, publish a job from the demo terminal, see the metric update within ~200ms (no 2s lag).
- Network tab: hub frames carry the event; REST refetches happen on the event, not on a timer.
- Disable `AddDashboardPush()` server-side, reload, repeat — fallback polling (now at 30s safety-net cadence) still works.
- Two browser tabs open — both update simultaneously.

## Batch 5 — Docs

### Steps

1. **`README.md`** — add a "Realtime dashboard push" section near the existing "DB Push" section. Key points:
   - Opt-in via `opt.AddDashboardPush()` after `AddWarp<TContext>(...)`.
   - For multi-server fanout, also call `opt.UseDatabasePush()`. Without it, push is single-server only.
   - Frontend probes a `/api/dashboard/push/probe` endpoint and falls back to polling if disabled.
   - Auth flows through the same `IWarpAuthorizationFilter`.

2. **`CLAUDE.md`** — append:
   - §2.10 documenting the addon (one paragraph mirroring §2.9 DB-push entry).
   - In "Workers / Background tasks" section, note `DashboardBroadcaster` as a third consumer of `ServerTaskSignals` alongside `Orchestrator` and `MessageRouter`.

**Checkpoint:** `dotnet build src/Warp.slnx` (rebuilds the SPA via csproj target) + `dotnet test --project src/tests/Warp.Tests/Warp.Tests.csproj` (~1m 30s) both green.

## Verification across all batches

After batch 5: write the behavioral diff. Confirm the spec's `change_manifest` matches `git diff --name-only HEAD`. Run the architecture-reviewer and test-reviewer in parallel.

## Sequencing options

- **Strict order:** 1 → 2 → 3 → 4 → 5. Easiest to checkpoint.
- **Parallel:** 1 and 3 can land independently in parallel since 3 only depends on the probe endpoint (which is in 1). 2 depends on 1. 4 depends on 3. 5 last.
- Recommended: strict order. Smaller PRs, easier review. 5 batches × ~1 day each → ~3–5 days realistic, single engineer.
