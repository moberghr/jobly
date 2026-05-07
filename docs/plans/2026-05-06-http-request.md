# Plan: HTTP Exposure for Warp Handlers

Spec: [`docs/specs/2026-05-06-http-request.md`](../specs/2026-05-06-http-request.md).

`Warp.Http` is a standalone package — **independent of `Warp.UI`**. Scope is **`IRequest<T>` and `IStreamRequest<T>` only**; `IJob` and `IMessage` cannot be HTTP-exposed.

## Design decisions (and what we rejected)

### Attribute on the request type, not the handler

`[WarpHttpPost("/orders")]` lives on the request class. **Why:** the request type is the public contract; handlers are implementation. Warp already uses request-side metadata (`[Mutex]`, `[Retry]`). **Rejected:** Wolverine's handler-side model — fine for them because they don't have multi-handler messages, but wrong for Warp.

### Source generation for discovery

`Warp.Http.SourceGenerator` ships inside the `Warp.Http` NuGet (analyzers/dotnet/cs/), mirroring how `Warp.SourceGenerator` ships inside `Warp.Core`. Cross-assembly request discovery confirmed via `WarpMediatorGenerator.cs:435-471` — incremental generators walk `compilation.References`. **Why:** zero per-request reflection, AOT-friendly, consistent with the rest of Warp's discovery story. **Rejected:** runtime reflection (turns startup into a hot scan); a single mega-generator extending `Warp.SourceGenerator` (couples unrelated features).

### `IRequest<T>` / `IStreamRequest<T>` only — no `IJob` / `IMessage`

A type that implements `IJob` or `IMessage` and carries a `[WarpHttp...]` attribute is rejected at compile time via `WHTTP001`. **Why:** mixing fire-and-forget background semantics with synchronous HTTP request/response creates muddled response shape (200? 202?), muddled timing (await handler? return immediately?), and forces `Warp.Http` to take a dependency on `IPublisher`. The user-facing pattern is unambiguous: write a thin `IRequest<Guid>` whose handler calls `IPublisher.Enqueue` and returns the resulting job ID. **Rejected:** the earlier "tag any handler" angle — clever-looking but creates more confusion than convenience.

### Lean on ASP.NET conventions for binding, but emit the binding code

System.Text.Json for body via the host's `Microsoft.AspNetCore.Http.Json.JsonOptions`; generated property reads for query/route/header. **Records with primary constructors are first-class** — generator detects record types or types without parameterless ctors and binds positionally through the canonical ctor. Per-parameter sources work as expected: a record `(Guid Id, string Name, List<LineItem> Items)` on `[WarpHttpPost("/orders/{id}")]` binds `Id` from the route token and `Name`/`Items` from the body (the body deserializer sees a payload shape that excludes route/query/header parameters). Nullability follows C# semantics — non-nullable reference type missing → 400; nullable → null; value type → default. **GET/DELETE never read body** (`WHTTP003`). **Rejected:** depending on `Microsoft.AspNetCore.Mvc.Core` for model binding; a Warp-specific JSON options override (premature).

### No HTTP-aware pipeline opt-out — existing behaviors self-scope

Confirmed at `src/core/Warp.Core/Retry/RetryPipelineBehavior.cs:8-9` and `Mutex/MutexPipelineBehavior.cs:23`. Retry has `where TRequest : IRequest<TResponse>, IJob` — and since HTTP can't expose `IJob`, retry never instantiates on the HTTP path. Mutex bypasses for non-IJob. So no Wolverine-style `MiddlewareScoping` enum is needed. User-defined behaviors that misbehave on HTTP are the user's problem.

### Imperative `IHttpResponseShape`, not return-value `IResult`

Domain types implement `void Apply(HttpContext)` to set status/headers/Location. **Applies to `IRequest<TResponse>` non-Unit responses only** — not to `IRequest<Unit>` (204), not to streams (status fixed for stream lifetime). **Why:** matches Wolverine's `IHttpAware`; lets users return plain DTOs from handlers; doesn't couple handlers to ASP.NET. **Rejected:** `IResult` returns from handlers (couples handlers to ASP.NET, breaks the in-memory mediator path).

### Named groups for selective registration, no predicate filter

`[WarpHttpPost("/orders", Group = "public")]` declares group membership. `MapWarpHttp(string? group = null)` registers descriptors with strictly-matching group (null matches null). Multiple `MapWarpHttp` calls on different `IEndpointRouteBuilder` groups are the canonical multi-prefix story:

```csharp
app.MapWarpHttp();                                          // null-group only
app.MapGroup("/api/public").RequireAuthorization("publicPolicy").MapWarpHttp("public");
app.MapGroup("/internal/admin").RequireAuthorization("adminPolicy").MapWarpHttp("admin");
```

Calling `MapWarpHttp(group)` twice on the same builder with the same group throws `InvalidOperationException` at startup. `[Authorize]` / `[AllowAnonymous]` on the request type compose into endpoint metadata. **Rejected:** predicate filter (vetoed); silent double-registration (foot-gun).

### Multi-attribute allowed; explicit Name required when multiple

`AllowMultiple = true` on `WarpHttpAttribute`. When multi-attribute and any attribute lacks `Name`, the generator emits `WHTTP002` (ASP.NET requires unique route names). **Rejected:** auto-derived names — too magical.

### Standard ASP.NET error handling, route constraints pass-through

Binding failures surface as `BadHttpRequestException` / JSON exceptions; whatever exception middleware the host has configured produces the response. Route template constraints like `/orders/{id:int}` are pure ASP.NET — we just pass the template through. **Rejected:** Warp-specific error envelope; reimplementing route constraints.

## Architecture sketch

```
Generator (compile time):
  for each type T in compilation with [WarpHttp...]:
    classify HandlerKind from T's interfaces (Request | Stream)
      → if T implements IJob or IMessage, or none of IRequest<>/IStreamRequest<>: emit WHTTP001
    if multiple [WarpHttp...] attributes and any lacks Name: emit WHTTP002
    if [FromBody] on a property and verb is GET/DELETE only: emit WHTTP003
    emit a static class WarpHttp_<T>_<index> { public static RequestDelegate Delegate(IServiceProvider) ... }
    add an entry to WarpGeneratedHttpRegistry via [ModuleInitializer]

Startup:
  AddWarpHttp() — registers options, JSON writer, SSE writer
  MapWarpHttp(group) — for each registry entry where descriptor.Group == group:
                       endpoints.MapMethods(route, [method], delegate)
                         .Accepts<TReq>("application/json") (body verbs)
                         .Produces<TRes>(status)
                         .WithName(name) when set
                         .WithTags(firstRouteSegment)
                         .WithMetadata(authAttribute) for any [Authorize]/[AllowAnonymous]
                       throws if (group, builder) was already mapped

Per request (no reflection on this path):
  generated RequestDelegate
    → bind TRequest (body | route | query | header; primary-ctor or property-set)
    → dispatch by kind: IMediator.Send | IMediator.CreateStream
    → write response (JSON | 204 | SSE)
    → IHttpResponseShape.Apply if Request kind, response is non-Unit, and implements it
```

## Implementation batches

### Batch 1 — Source-gen scaffolding + attribute + descriptor (NoDb)
**Product:** `Warp.Http.csproj`, `Warp.Http.SourceGenerator.csproj`, `WarpHttpAttribute` (with `Method`, `Route`, `Group`, `Name`; `AllowMultiple = true`) + verb subclasses, `WarpHttpOptions.cs`, `Discovery/HttpEndpointDescriptor.cs`, `Discovery/HandlerKind.cs` (only `Request` and `Stream` values), `Discovery/WarpGeneratedHttpRegistry.cs`, `ServiceCollectionExtensions.AddWarpHttp` (shell), `EndpointRouteBuilderExtensions.MapWarpHttp(string? group)` (shell — iterates registry filtered by group).
**Generator:** `WarpHttpGenerator.cs` (incremental pipeline), `Diagnostics.cs` (`WHTTP001` — must be IRequest/IStreamRequest, must NOT be IJob/IMessage; rule emitted in this batch).
**Solution:** add both projects to `Warp.slnx`. Add `Warp.Http` ref + `Microsoft.AspNetCore.Mvc.Testing` package ref to `Warp.Tests.csproj`. Mirror `Warp.SourceGenerator → Warp.Core` packing.
**Test infrastructure:** `Http/WarpHttpTestApp.cs` — `WebApplicationFactory`-backed fluent builder; `Http/TestHandlers.cs` shell.
**Tests:** `Http/AttributeDiscoveryTests.cs` (descriptor model + group filtering); `Http/DiagnosticsTests.cs` with `WHTTP001` cases (IJob, IMessage, unrelated types, dual-implementation).
**Checkpoint:** `dotnet build Warp.slnx` clean; NoDb tests green.

### Batch 2 — IRequest<T> HTTP path
**Product:** `Binding/FromRouteAttribute.cs`, `FromQueryAttribute.cs`, `FromHeaderAttribute.cs`, `FromBodyAttribute.cs`, `Dispatch/JsonResponseWriter.cs`.
**Generator:** `Emitters/BindingEmitter.cs` (body via STJ for POST/PUT/PATCH; route/query/header per attribute; primary-ctor positional vs property-set; nullability semantics; `WHTTP003` for `[FromBody]` on GET/DELETE-only — add `WHTTP003` to `Diagnostics.cs`). `Emitters/DelegateEmitter.cs` (Request branch, calls `IMediator.Send`, writes JSON or 204).
**Map:** `MapWarpHttp` registers endpoints; double-call throws.
**Tests:** `Http/RequestEndpointTests.cs`, `Http/VerbBehaviorTests.cs` (all five verbs end-to-end), `Http/BindingTests.cs` (incl. query arrays, URL-encoded, headers case-insensitive, route constraint pass-through), `Http/PipelineBehaviorTests.cs` (custom behavior runs/short-circuits/orders), `Http/HandlerErrorTests.cs` (throws/null/cancelled), `Http/DiagnosticsTests.cs` extended with `WHTTP003`.
**Checkpoint:** NoDb suite green; record + class shapes both work; `Unit` returns 204; binding failure → 400.

### Batch 3 — IStreamRequest<T> HTTP path
**Product:** `Dispatch/SseResponseWriter.cs`.
**Generator:** extend `DelegateEmitter` with Stream branch — `IMediator.CreateStream`, SSE framing, `RequestAborted` propagation.
**Tests:** `Http/StreamEndpointTests.cs` (happy-path framing, mid-stream cancellation), `Http/StreamEdgeCasesTests.cs` (empty, single-item, error mid-stream, large-N back-pressure).
**Checkpoint:** NoDb suite green.

### Batch 4 — Multi-attribute, groups, auth metadata, OpenAPI
**Generator:** emit `WHTTP002` (added to `Diagnostics.cs`); one descriptor + one delegate per `[WarpHttp...]` attribute.
**Map:** `.Accepts<TRequest>()` (body verbs), `.Produces<TResponse>(status)`, `.WithName` when set, `.WithTags(<first route segment>)`. Pull `[Authorize]` / `[AllowAnonymous]` from request type and attach as endpoint metadata.
**Tests:** `Http/MultiAttributeTests.cs`, `Http/GroupRegistrationTests.cs` (incl. double-call throw), `Http/AuthMetadataTests.cs` (metadata + group-level RequireAuthorization composition + per-request override), `Http/DiagnosticsTests.cs` extended with `WHTTP002`.
**Checkpoint:** NoDb suite green; OpenAPI metadata correct in demo app.

### Batch 5 — IHttpResponseShape + README + demo app slice
**Product:** `IHttpResponseShape.cs`. Update `JsonResponseWriter` to call `Apply(HttpContext)` when response is non-null and implements `IHttpResponseShape`. `SseResponseWriter` does NOT call Apply.
**Tests:** `Http/IHttpResponseShapeTests.cs` — Apply runs for `IRequest<T>` non-Unit; not for streams; not for 204.
**Demo:** extend `src/demo/Warp.TestApp/` with `AddWarpHttp()` + `MapWarpHttp()` and one HTTP-exposed handler per shape (`IRequest<T>` non-Unit, `IRequest<Unit>`, `IStreamRequest<T>`, plus the "submit-a-job-via-HTTP wrapper" `IRequest<Guid>` calling `IPublisher.Enqueue`).
**Docs:** README section.
**Checkpoint:** Full suite green; README reads well; demo app runs.

## Verification plan

- `dotnet build Warp.slnx` warning-clean (StyleCop / Roslynator / Sonar / Meziantou) after every batch.
- `dotnet test --filter-trait "Category=NoDb"` after every batch (~3s feedback). All HTTP tests are NoDb.
- Full suite once before review.
- Behavioral diff written before Stage 1 review.

## Open questions for the engineer (smaller polish items)

1. **Generator NuGet packing** — mirror `Warp.SourceGenerator → Warp.Core`. Confirm exact recipe in Batch 1 by reading `Warp.Core.csproj`.
2. **`IHttpResponseShape` namespace** — currently `Warp.Http.IHttpResponseShape`. Could nest under `Warp.Http.Dispatch.IHttpResponseShape`. Top-level for discoverability.
3. **Demo app rename to samples** — out of scope for this feature; follow-up PR.
