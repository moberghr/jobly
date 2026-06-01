# Code Index

> Capability index — what the codebase can do, not where files live.
> Refresh: `/mtk audit duplicates` (or `bash scripts/build-code-index.sh` if present).
> Last built: 2026-05-29 (setup-bootstrap seed). Stale after 30 days or >50 commits.

## Registration & Hosting

| Capability | Entry point | Notes |
|---|---|---|
| Register Warp core (job store, publishers) | `src/core/Warp.Core/ServiceConfiguration.cs:AddWarp` | Generic over the consumer's `DbContext`; the public entry — don't hand-wire publishers. |
| Wire Warp EF interceptors | `src/core/Warp.Core/ServiceConfiguration.cs:AddWarpInterceptors` | Attach to `DbContextOptionsBuilder`; concurrency-token + save interceptors. |
| Register Warp worker (dispatch loop) | `src/core/Warp.Worker/ServiceConfiguration.cs:AddWarpWorker` | Runs the dequeue/dispatch background service; generic over `DbContext`. |
| Register source-generated mediator | `src/core/Warp.SourceGenerator/WarpMediatorGenerator.cs:AddWarpMediator` | Emitted at compile time — do NOT add reflection-based handler registration. |

## HTTP & Dashboard

| Capability | Entry point | Notes |
|---|---|---|
| Register HTTP-exposed handlers | `src/core/Warp.Http/ServiceCollectionExtensions.cs:AddWarpHttp` | Pairs with `[WarpHttp]` attribute + `Warp.Http.SourceGenerator`. |
| Map HTTP handler endpoints | `src/core/Warp.Http/EndpointRouteBuilderExtensions.cs:MapWarpHttp` | Minimal-API routing, not MVC controllers. |
| Mount dashboard UI middleware | `src/core/Warp.UI/UIMiddleware/WarpUIBuilder.cs:UseWarpUI` | Serves the React SPA + SignalR push. |
| Map dashboard API endpoints | `src/core/Warp.UI/Endpoints/WarpEndpoints.cs:MapWarpApiEndpoints` | Backs the dashboard data tables. |

## Mediation

| Capability | Entry point | Notes |
|---|---|---|
| Send request → response | `src/core/Warp.Core/Handlers/IMediator.cs:Send` | In-memory request/response; the framework's own `IMediator` (not MediatR). |
| Create response stream | `src/core/Warp.Core/Handlers/IMediator.cs:CreateStream` | `IAsyncEnumerable<T>` streaming requests. |

## Persistence Providers

| Capability | Entry point | Notes |
|---|---|---|
| Use PostgreSQL provider | `src/core/providers/Warp.Provider.PostgreSql/PostgreSqlServiceConfiguration.cs:UsePostgreSql` | Provides `FOR UPDATE SKIP LOCKED` dequeue SQL — keep dialect SQL here, not in Warp.Core. |
| Use SQL Server provider | `src/core/providers/Warp.Provider.SqlServer/SqlServerServiceConfiguration.cs:UseSqlServer` | SQL Server dialect of the dequeue path; a data-layer change must work on both providers. |
