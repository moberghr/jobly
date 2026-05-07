# Lessons Learned

> This file captures patterns and mistakes discovered during AI-assisted development.
> It is read at the start of every `/project:moberg-implement` session.
> Commit this file — it is institutional memory for the team.

## 2026-04-09 — Naming conventions & schema support

- EF Core's `.ToTable(nameof(Entity))` via Fluent API sets the table name with `ConfigurationSource.Explicit`, which prevents `EFCore.NamingConventions` from transforming it (conventions have lower priority than explicit config). To let naming conventions work, don't call `.ToTable()` — let EF Core use the CLR type name as the default (convention-sourced).
- When Respawn's `TablesToIgnore` uses hardcoded table names, they break when naming conventions transform those names. Resolve table names from the EF model instead (`model.FindEntityType(typeof(Entity))!.GetTableName()`).
- Using `entity.Metadata.SetSchema(schema)` sets schema without re-pinning the table name — this is the correct way to assign schema while preserving naming convention freedom. Don't use `.ToTable(name, schema)` which sets both explicitly.
- `null`-coalescing (`??`) on config properties silently discards explicit `null` values. When a user sets `Schema = null` to opt out, `config?.Schema ?? "warp"` ignores their intent. Use `config != null ? config.Schema : "warp"` to distinguish "no config" from "config with null".
- The `SqlServerRowLockInterceptor` hardcoded table names for string replacement. Use regex against the `FROM [table] AS [alias]` pattern instead, so it works regardless of naming convention or schema.

## 2026-04-09 — Multi-server integration tests

- `IBatchPublisher.StartNew()` and `ContinueBatchWith()` do NOT auto-save. Always call `batchPublisher.SaveChangesAsync()` after batch operations. The publisher and batch publisher are separate DI scopes — `publisher.SaveChangesAsync()` does not save the batch publisher's changes.
- Batch continuations are nested batches (Kind=Batch with ParentId=originalBatchId), not direct children. When asserting batch structure, query continuation batch children separately.
- Don't assert that "both servers processed some jobs" in multi-server tests. Warp provides no fairness guarantee — competitive fetch-and-lock means one server can win all fetches. Test correctness (no duplicates), not load distribution.
- Always await cleanup of CancellableRequest after `DeleteJob` — call `WaitForJobState(id, State.Deleted)` to ensure the handler exits before the next test runs.

## 2026-04-09 — Dashboard demo mode & screenshots

- Axios custom adapters receive `config.url` with the `baseURL` already resolved (full path like `/warp/api/status`, not just `/status`). Strip the baseURL prefix before pattern-matching in mock adapters.
- Vite `server.proxy: undefined` inside a `server: {}` object does NOT disable the proxy — the `undefined` value is ignored and defaults apply. To conditionally disable proxy, return separate objects: `server: isDemo ? {} : { proxy: { ... } }`.
- When using `reuseExistingServer: true` in Playwright, stale Vite dev servers on the same port from previous runs will be reused even if the config changed. Use a distinct port for the screenshot Vite server to avoid conflicts.
- `State.Scheduled` does not exist in the frontend type system — scheduled jobs have `currentState: State.Enqueued` with a future `scheduleTime`. The `/jobs/scheduled` endpoint returns enqueued jobs filtered by schedule time on the backend.

## 2026-05-06 — Warp.Http source generator

- `MapMethods(IEndpointRouteBuilder, string, IEnumerable<string>, RequestDelegate)` returns `IEndpointConventionBuilder` (no `.Accepts<>` / `.Produces<>`). The `Delegate`-overload `MapMethods(string, IEnumerable<string>, Delegate)` returns `RouteHandlerBuilder` with the full Minimal API metadata API. To get the metadata methods while keeping our own RequestDelegate, wrap as `(HttpContext ctx) => requestDelegate(ctx)` — Minimal API recognises HttpContext as a known parameter and injects it directly.
- Generators authoring `WriteAsync` calls into emitted code: `HttpResponse.WriteAsync(string)` is an extension method in `Microsoft.AspNetCore.Http.HttpResponseWritingExtensions`. Generated code in user assemblies often lacks the right `using`, so emit fully-qualified: `global::Microsoft.AspNetCore.Http.HttpResponseWritingExtensions.WriteAsync(ctx.Response, ...)`.
- Records with parameterless construction syntax (`record EmptyStream;`) do have a synthesized parameterless ctor. If `BindingEmitter` returns `WholeBody` for empty types on a body-less verb, JsonSerializer fails on the empty body. Fix: empty-target types use `ParameterlessCtor` mode with no binding reads — emit `new TRequest()` and dispatch.
- `WarpGeneratedHttpRegistry` is `[ModuleInitializer]`-populated and never cleared. Tests that read `Snapshot()` directly must not also call `Add()`, or counts in other tests silently shift. Assertions like `descriptors.Count == 2` are global-state-coupled by design — mirror this constraint when adding new tests.
- ASP.NET `BadHttpRequestException` thrown inside a `RequestDelegate` is not auto-translated to a 400 by TestHost (some hosts do, some don't). Wrap the generated body in `try/catch (BadHttpRequestException ex) { ctx.Response.StatusCode = ex.StatusCode == 0 ? 400 : ex.StatusCode; ... }` so behavior is deterministic regardless of host exception middleware.
- Sonar S2699 ("at least one assertion") fires even when the test relies on `[TimedFact]` deadline as the implicit pass criterion. Add an explicit elapsed-time assertion (e.g. `sw.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2))`) or a state-flag assertion to keep the analyzer quiet AND give the test real teeth.
- Source-generator project references with `OutputItemType="Analyzer" ReferenceOutputAssembly="false"` block the test project from instantiating generator types via reflection. To drive the generator from tests via `CSharpGeneratorDriver`, omit `ReferenceOutputAssembly="false"` (single ref serves both as analyzer at compile time AND as a referenced assembly for tests).
- When you put `[WarpHttp...]`-tagged types into a project that's referenced by another project that ALSO runs the Warp source generator, both compilations emit `Warp.Core.Handlers.Generated.WarpMediatorServiceExtensions` and you get CS0436 conflicts. Keep handler types in ONE project of the dependency chain.
