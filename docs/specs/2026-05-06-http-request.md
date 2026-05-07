# Spec: HTTP Exposure for Warp Handlers

## Problem

Warp's `IRequest<TResponse>` / `IStreamRequest<TResponse>` handlers are today invoked only via `IMediator.Send` / `IMediator.CreateStream`. Users wanting to expose one of these handlers over HTTP must hand-write an ASP.NET Minimal API endpoint that deserializes the request, calls the mediator, and serializes the response. That's friction Warp can remove — the handler already declares its request/response contract.

Two reference designs were studied:

- **Wolverine.Http** — attribute-driven (`[WolverinePost("/orders")]`), source-generated dispatch, pluggable parameter binding, unified pipeline shared with the message handler runtime, plugs into ASP.NET `EndpointDataSource` with full OpenAPI metadata.
- **FastEndpoints** — class-per-endpoint (`Endpoint<TReq, TRes>` + `Configure()`), compiled-expression binding, FluentValidation pipelined inline, imperative response sending via `Send.OkAsync(...)`.

**Scope is `IRequest<T>` and `IStreamRequest<T>` only.** `IJob` and `IMessage` (background work) are explicitly **not** HTTP-exposable via this feature. Users who want "submit a job via HTTP" write a thin `IRequest<Guid>` whose handler calls `IPublisher.Enqueue` and returns the resulting job ID — that keeps the synchronous request/response semantics of HTTP separate from the fire-and-forget semantics of background work, and avoids the muddled "is this 200 or 202?" question.

`Warp.Http` is **independent of `Warp.UI`**. `Warp.UI` ships its own dashboard endpoints under `/warp` and is unrelated to this feature.

## Solution (MVP)

A new `Warp.Http` package that lets users tag a request type with `[WarpHttp(...)]` and registers it as an ASP.NET endpoint at startup. **Discovery via source generation** — `Warp.Http.SourceGenerator` ships in the same NuGet, scans the consuming assembly for `[WarpHttp...]` attributes, and emits a strongly-typed registry. This matches the existing `Warp.SourceGenerator` pattern (handler discovery, mediator dispatch) and gives zero per-request reflection cost.

### User-facing API

```csharp
// 1. Tag the request — record with primary ctor is fine
[WarpHttpPost("/orders")]
public record CreateOrder(string CustomerName, List<LineItem> Items) : IRequest<OrderDto>;

public class CreateOrderHandler : IRequestHandler<CreateOrder, OrderDto>
{
    public async Task<OrderDto> HandleAsync(CreateOrder request, CancellationToken ct) { ... }
}

// 2. Streaming
[WarpHttpGet("/orders/feed")]
public class OrderFeed : IStreamRequest<OrderEvent> { }
// Streamed to client as text/event-stream.

// 3. "Submit a job via HTTP" — the user writes a thin IRequest wrapper. No magic.
[WarpHttpPost("/reports/generate")]
public record EnqueueReport(Guid TenantId) : IRequest<Guid>;

public class EnqueueReportHandler(IPublisher publisher) : IRequestHandler<EnqueueReport, Guid>
{
    public Task<Guid> HandleAsync(EnqueueReport req, CancellationToken ct)
        => publisher.Enqueue(new GenerateReportJob(req.TenantId));
}

// 4. Multi-route (versioning aliases — AllowMultiple = true; explicit Name required)
[WarpHttpPost("/v1/orders", Name = "CreateOrderV1")]
[WarpHttpPost("/v2/orders", Name = "CreateOrderV2")]
public record CreateOrderMulti(string CustomerName) : IRequest<OrderDto>;

// 5. Named groups for selective registration
[WarpHttpPost("/orders", Group = "public")]
public record CreateOrderPublic(string CustomerName) : IRequest<OrderDto>;

[WarpHttpPost("/admin/users", Group = "admin")]
public record CreateAdminUser(string Email) : IRequest<Guid>;

// 6. Auth via standard ASP.NET attributes (surfaced as endpoint metadata)
[Authorize(Policy = "OrdersWrite")]
[WarpHttpPost("/orders/cancel")]
public record CancelOrder(Guid Id) : IRequest<Unit>;

// 7. Registration
builder.Services.AddWarpHttp();
app.MapWarpHttp();                                          // null-group descriptors
app.MapGroup("/api/public").RequireAuthorization("publicPolicy").MapWarpHttp("public");
app.MapGroup("/internal/admin").RequireAuthorization("adminPolicy").MapWarpHttp("admin");
```

`MapWarpHttp(string? group = null)` registers descriptors whose `Group` strictly equals the argument (null matches null). No overlap, no implicit defaults — descriptors with `Group = "x"` only register when `MapWarpHttp("x")` is called. `[Authorize]` / `[AllowAnonymous]` on the request type are surfaced as ASP.NET endpoint metadata, so group-level `RequireAuthorization()` plus per-request `[AllowAnonymous]` compose normally.

### Response semantics by handler kind

| Handler kind             | Status | Body                                        |
|--------------------------|--------|---------------------------------------------|
| `IRequest<TResponse>`    | 200    | JSON of `TResponse`                         |
| `IRequest<Unit>`         | 204    | empty                                       |
| `IStreamRequest<T>`      | 200    | `text/event-stream` (one `data:` per item)  |

A response type can implement `IHttpResponseShape` — an `Apply(HttpContext)` hook — to override status code / set headers / set Location, mirroring Wolverine's `IHttpAware`. **Apply runs only for `IRequest<TResponse>` where `TResponse != Unit`** — there's no response object to shape on `IRequest<Unit>` (204) or on streams (status fixed for the stream lifetime).

**`IJob` and `IMessage` cannot be HTTP-exposed.** A type that implements either of those interfaces and carries a `[WarpHttp...]` attribute is rejected at compile time via diagnostic `WHTTP001`.

### Binding

For v1, lean on ASP.NET conventions rather than rolling our own binder.

- **POST/PUT/PATCH:** request body deserialized into `TRequest` via `System.Text.Json`. The generator emits a per-request delegate that reads `IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>` from the request scope and uses its `SerializerOptions` — i.e., whatever the host has configured for Minimal API JSON. No Warp-specific JSON options surface in v1.
- **GET/DELETE:** request properties bound from route + query string + headers only. **No request body is read on GET or DELETE in v1**, even with `[FromBody]` on a property — the generator emits a Roslyn diagnostic if `[FromBody]` appears on a property of a GET/DELETE-only request type.
- **Per-property attribute overrides:** `[FromRoute]`, `[FromQuery]`, `[FromHeader]` (Warp's own thin attributes, value-compatible with ASP.NET semantics) override defaults. Attribute-less properties on a body-verb request bind from the body. Attribute-less properties on a GET/DELETE request bind from the query string by name; properties whose name matches a route token bind from the route.

The source generator emits the binding code per request type — no runtime reflection on the hot path.

**Binding errors** (malformed JSON, unparseable int, missing required route value) surface as the standard ASP.NET `BadHttpRequestException` / JSON exceptions. Whatever exception middleware / `AddProblemDetails` / `UseExceptionHandler` the host has configured produces the response. Warp.Http does not impose its own error envelope.

Out of v1: form binding, multipart, file uploads, claim/principal binding, IParameterStrategy plugin model.

### Pipeline integration

The HTTP entry point calls `IMediator.Send` (for `IRequest`) or `IMediator.CreateStream` (for streams). Both already run the existing `IPipelineBehavior` / `IStreamPipelineBehavior` chain, so auth / validation / logging behaviors get reused for free without any HTTP-specific pipeline.

`RetryPipelineBehavior` (constraint `where TRequest : IRequest<TResponse>, IJob`) and `MutexPipelineBehavior` (runtime `is not IJob` check) self-scope and never run on the HTTP path — both require `IJob`, which can't be HTTP-exposed by definition.

### Discovery (source generation)

`Warp.Http.SourceGenerator` is an incremental generator packaged with `Warp.Http`. For each type in the compilation that carries a `WarpHttpAttribute`:

1. Validate the type implements `IRequest<TResponse>` or `IStreamRequest<TResponse>` and does **not** implement `IJob` or `IMessage` (else `WHTTP001`).
2. Validate per-attribute rules (`WHTTP002` for multi-attribute without `Name`, `WHTTP003` for `[FromBody]` on GET/DELETE-only requests).
3. Emit a static `RequestDelegate` factory that:
   - Reads/binds `TRequest` from the `HttpContext` (body / query / route / header per attributes; primary-ctor positional or property setters depending on type shape).
   - Calls `IMediator.Send` (for `IRequest`) or `IMediator.CreateStream` (for streams).
   - Writes the response (JSON / 204 / SSE).
4. Aggregate the per-type factories into a `WarpGeneratedHttpRegistry.Endpoints` collection (mirroring `WarpGeneratedHandlerRegistry`).
5. Register via `[ModuleInitializer]` so the registry is populated at assembly load.

`AddWarpHttp()` flushes the registry into options; `MapWarpHttp(IEndpointRouteBuilder, string? group = null)` walks it and calls `endpoints.MapMethods(route, [method], requestDelegate)` per descriptor whose group strictly matches, attaching ASP.NET endpoint metadata. Calling `MapWarpHttp(group)` twice on the same builder with the same group throws `InvalidOperationException` at startup.

### OpenAPI and endpoint metadata

Each endpoint attaches metadata via the standard ASP.NET extension methods:
- `.Accepts<TRequest>("application/json")` (POST/PUT/PATCH only)
- `.Produces<TResponse>(200)` for `IRequest<TResponse>` non-Unit; `.Produces(204)` for `IRequest<Unit>`; for streams, `.Produces<TResponse>(200, "text/event-stream")`
- `.WithName(...)` — type name when the request has a single `[WarpHttp...]`; when multiple attributes are present, name comes from the attribute's `Name` property (no default — emitted as a Roslyn diagnostic if multi-attribute and any attribute lacks `Name`)
- `.WithTags(...)` — derived from the route's first segment unless overridden via the attribute
- `[Authorize]` / `[AllowAnonymous]` attributes on the request type are pushed onto the endpoint as ASP.NET metadata so they compose with group-level `RequireAuthorization()`

Swashbuckle / NSwag / `Microsoft.AspNetCore.OpenApi` pick this up automatically.

### Generator diagnostics

The source generator emits these compile-time errors to catch the common mistakes:

| ID        | Severity | Condition |
|-----------|----------|-----------|
| `WHTTP001` | Error    | Type tagged with `[WarpHttp...]` must implement `IRequest<TResponse>` or `IStreamRequest<TResponse>` and must NOT implement `IJob` or `IMessage`. |
| `WHTTP002` | Error    | Multiple `[WarpHttp...]` attributes on the same type without explicit `Name = "..."` on each. |
| `WHTTP003` | Error    | `[FromBody]` on a property of a request type whose only attributes are `[WarpHttpGet]` / `[WarpHttpDelete]`. |

## Architecture

```
HTTP request
  └─► generated RequestDelegate (one per [WarpHttp...] descriptor, emitted by source-gen)
        ├─► bind TRequest (body | route | query | header — generated, no reflection)
        ├─► dispatch by HandlerKind:
        │     ├─ Request → IMediator.Send         → 200 + JSON | 204
        │     └─ Stream  → IMediator.CreateStream → 200 + SSE
        └─► IHttpResponseShape.Apply if response implements it (Request kind only, non-Unit)
```

## Scope Classification

**Substantial feature.** New top-level package, new public surface, new source generator, new ASP.NET integration. Spec / plan / approval gate required.

## Change Manifest

### New files — Warp.Http
- `src/core/Warp.Http/Warp.Http.csproj`
- `src/core/Warp.Http/WarpHttpAttribute.cs` — base attribute (`AllowMultiple = true`) + verb subclasses (`WarpHttpGet`, `WarpHttpPost`, `WarpHttpPut`, `WarpHttpPatch`, `WarpHttpDelete`); attribute properties: `Method`, `Route`, `Group`, `Name`
- `src/core/Warp.Http/WarpHttpOptions.cs`
- `src/core/Warp.Http/Binding/FromRouteAttribute.cs`
- `src/core/Warp.Http/Binding/FromQueryAttribute.cs`
- `src/core/Warp.Http/Binding/FromHeaderAttribute.cs`
- `src/core/Warp.Http/Binding/FromBodyAttribute.cs` — explicit body opt-in (POST/PUT/PATCH only; diagnostic on GET/DELETE)
- `src/core/Warp.Http/Discovery/HttpEndpointDescriptor.cs`
- `src/core/Warp.Http/Discovery/HandlerKind.cs`
- `src/core/Warp.Http/Discovery/WarpGeneratedHttpRegistry.cs`
- `src/core/Warp.Http/Dispatch/JsonResponseWriter.cs`
- `src/core/Warp.Http/Dispatch/SseResponseWriter.cs`
- `src/core/Warp.Http/IHttpResponseShape.cs`
- `src/core/Warp.Http/ServiceCollectionExtensions.cs` — `AddWarpHttp()`
- `src/core/Warp.Http/EndpointRouteBuilderExtensions.cs` — `MapWarpHttp(string? group = null)`

### New files — Warp.Http.SourceGenerator
- `src/core/Warp.Http.SourceGenerator/Warp.Http.SourceGenerator.csproj`
- `src/core/Warp.Http.SourceGenerator/WarpHttpGenerator.cs` — incremental generator
- `src/core/Warp.Http.SourceGenerator/HttpEndpointModel.cs` — internal type for the pipeline
- `src/core/Warp.Http.SourceGenerator/Diagnostics.cs` — `WHTTP001` / `WHTTP002` / `WHTTP003` descriptors
- `src/core/Warp.Http.SourceGenerator/Emitters/DelegateEmitter.cs` — emits the `RequestDelegate` per descriptor
- `src/core/Warp.Http.SourceGenerator/Emitters/BindingEmitter.cs` — emits body/route/query/header binding
- `src/core/Warp.Http.SourceGenerator/Emitters/RegistryEmitter.cs` — emits the `[ModuleInitializer]` registry block

### New test files
- `src/tests/Warp.Tests/Http/AttributeDiscoveryTests.cs` — generator descriptor model, group filtering, basic registration
- `src/tests/Warp.Tests/Http/DiagnosticsTests.cs` — per-diagnostic positive + negative cases (`WHTTP001`/`002`/`003`), edge cases like a type that implements both `IRequest<T>` and `IJob`
- `src/tests/Warp.Tests/Http/RequestEndpointTests.cs` — `IRequest<T>` 200/JSON; `IRequest<Unit>` 204; record + class shapes
- `src/tests/Warp.Tests/Http/VerbBehaviorTests.cs` — GET/POST/PUT/PATCH/DELETE end-to-end with appropriate request shapes
- `src/tests/Warp.Tests/Http/BindingTests.cs` — body/route/query/header combinations; primary-ctor positional binding; nullability matrix; route template constraint pass-through (`{id:int}` rejects non-int → 404); query array params; URL-encoded values; case-insensitive header match
- `src/tests/Warp.Tests/Http/StreamEndpointTests.cs` — happy-path SSE framing; cancellation on `RequestAborted`
- `src/tests/Warp.Tests/Http/StreamEdgeCasesTests.cs` — empty stream (zero items), single-item, error mid-stream, large-N back-pressure
- `src/tests/Warp.Tests/Http/PipelineBehaviorTests.cs` — custom `IPipelineBehavior` runs on HTTP path, can short-circuit, auth-style 401, ordering preserved
- `src/tests/Warp.Tests/Http/HandlerErrorTests.cs` — handler throws (host exception middleware sees it), handler returns null (204 vs 200-with-null), cancelled handler (`OperationCanceledException` → ASP.NET cancellation handling)
- `src/tests/Warp.Tests/Http/IHttpResponseShapeTests.cs` — `Apply` runs for `IRequest<T>` non-Unit only; not for streams; not for 204 (no response object)
- `src/tests/Warp.Tests/Http/MultiAttributeTests.cs` — `AllowMultiple = true`; multi-route registration; OperationId/Name disambiguation
- `src/tests/Warp.Tests/Http/GroupRegistrationTests.cs` — strict group matching; non-overlapping; `MapWarpHttp` double-call throws
- `src/tests/Warp.Tests/Http/AuthMetadataTests.cs` — `[Authorize]` / `[AllowAnonymous]` surface as endpoint metadata; group-level `RequireAuthorization()` composes; per-request `[AllowAnonymous]` overrides group auth
- `src/tests/Warp.Tests/Http/TestHandlers.cs` — shared test request types and handlers
- `src/tests/Warp.Tests/Http/WarpHttpTestApp.cs` — `WebApplicationFactory`-based helper. Per-test fluent builder: `WarpHttpTestApp.Build(svc => svc.Add..., app => app.MapWarpHttp())` returns an `HttpClient` ready for assertions.

All HTTP tests are NoDb category — no `IJob`/`IMessage` HTTP path means no database dependency. Each test registers its own handlers in-memory and uses `WebApplicationFactory` (`Microsoft.AspNetCore.Mvc.Testing`) to spin up a real HTTP pipeline against TestServer.

### Modified files
- `src/Warp.slnx` — add `Warp.Http` and `Warp.Http.SourceGenerator` projects
- `src/tests/Warp.Tests/Warp.Tests.csproj` — reference `Warp.Http` (which transitively pulls the generator); add `Microsoft.AspNetCore.Mvc.Testing` package reference
- `src/demo/Warp.TestApp/Warp.Test.App.csproj` (or its `Program.cs` / Startup) — extend the existing demo app with one HTTP-exposed handler per shape (`IRequest<T>` non-Unit, `IRequest<Unit>`, `IStreamRequest<T>`, plus a "submit-a-job-via-HTTP wrapper" `IRequest<Guid>` whose handler calls `IPublisher.Enqueue`). Project is **not renamed** as part of this feature; rename to `Warp.SampleApp` is a follow-up.
- `README.md` — add a short section for HTTP exposure (scope: 1 section)

### Out of scope (deliberately deferred)
- Form / multipart / file binding.
- FluentValidation integration (users can add it as a pipeline behavior themselves).
- IParameterStrategy plugin model.
- OpenAPI customization beyond `Accepts` / `Produces` / `WithName` / `WithTags`.
- Auth opinions — users compose with `.RequireAuthorization()` at the ASP.NET layer.
- Anything related to `Warp.UI` (dashboard, status URLs, login). `Warp.Http` is standalone.

## Test Manifest

All HTTP tests are NoDb. Tests use `WebApplicationFactory<TEntryPoint>` from `Microsoft.AspNetCore.Mvc.Testing` (a thin layer over `Microsoft.AspNetCore.TestHost`) — gives a real HTTP pipeline with a `HttpClient` we can assert against. A shared `WarpHttpTestApp` helper exposes a fluent builder so each test specifies handlers and routing without copying boilerplate. No `[GenerateDatabaseTests]` is needed.

| Test class                       | Category | What it covers |
|----------------------------------|----------|----------------|
| `AttributeDiscoveryTests`        | NoDb     | Generator emits the right descriptors; verb subclasses; route templates; group filtering |
| `DiagnosticsTests`                | NoDb     | `WHTTP001` (positive: IJob/IMessage; unrelated type; type that implements both IRequest and IJob still rejected) (negative: pure IRequest passes); `WHTTP002` (multi-attribute Name missing/present); `WHTTP003` (`[FromBody]` on GET/DELETE) |
| `RequestEndpointTests`           | NoDb     | `IRequest<T>` 200 + JSON; `IRequest<Unit>` 204; record + class shapes |
| `VerbBehaviorTests`              | NoDb     | GET, POST, PUT, PATCH, DELETE end-to-end with appropriate request shapes |
| `BindingTests`                   | NoDb     | Body / route / query / header combinations; primary-ctor positional; nullability matrix; route constraints pass through; query arrays; URL-encoded values; case-insensitive headers |
| `StreamEndpointTests`            | NoDb     | Happy-path SSE framing; cancellation on `RequestAborted` |
| `StreamEdgeCasesTests`           | NoDb     | Empty stream; single-item; error mid-stream; large-N back-pressure |
| `PipelineBehaviorTests`          | NoDb     | Custom `IPipelineBehavior` runs; can short-circuit (e.g. 401 before handler); ordering preserved |
| `HandlerErrorTests`              | NoDb     | Handler throws → host exception middleware sees it; handler returns null; `OperationCanceledException` mid-handler |
| `IHttpResponseShapeTests`        | NoDb     | `Apply` runs for `IRequest<T>` non-Unit; not for streams; not for 204 |
| `MultiAttributeTests`            | NoDb     | `AllowMultiple = true`; multi-route end-to-end |
| `GroupRegistrationTests`         | NoDb     | Strict group matching; non-overlapping; double-`MapWarpHttp(group)` throws |
| `AuthMetadataTests`              | NoDb     | `[Authorize]` / `[AllowAnonymous]` surface as metadata; group-level `RequireAuthorization()` composes; per-request `[AllowAnonymous]` overrides |

## Implementation Batches

1. **Source-gen scaffolding + attribute + descriptor (NoDb)**
   - Both projects, attributes, descriptor, generator skeleton emitting an empty registry.
   - `WHTTP001` enforced (rejects `IJob`/`IMessage` and unrelated types).
   - `AttributeDiscoveryTests` green.
2. **`IRequest<T>` HTTP path**
   - `BindingEmitter` (body for POST/PUT/PATCH; query/route/header for all; primary-ctor positional or property-set; nullability per C# semantics; `WHTTP003` enforced).
   - `DelegateEmitter` for the Request kind. `JsonResponseWriter`.
   - `AddWarpHttp` / `MapWarpHttp` walk the registry and map endpoints. Double-call throws.
   - `RequestEndpointTests` + `BindingTests` green.
3. **`IStreamRequest<T>` HTTP path**
   - `SseResponseWriter`; `DelegateEmitter` Stream branch; `RequestAborted` propagation.
   - `StreamEndpointTests` green.
4. **Multi-attribute, groups, auth metadata, OpenAPI**
   - `WHTTP002` enforced; one descriptor per attribute.
   - `.Accepts` / `.Produces` / `.WithName` / `.WithTags` metadata; `[Authorize]` / `[AllowAnonymous]` surfaced.
   - `MultiAttributeTests` + `GroupRegistrationTests` + `AuthMetadataTests` green.
5. **`IHttpResponseShape` + README + demo app slice**
   - `IHttpResponseShape` hook in `JsonResponseWriter` (Request kind only, non-Unit).
   - `IHttpResponseShapeTests` green.
   - Extend `src/demo/Warp.TestApp/` with one HTTP-exposed handler per shape (`IRequest<T>` non-Unit, `IRequest<Unit>`, `IStreamRequest<T>`, plus the "submit-a-job-via-HTTP wrapper" pattern showing a handler that calls `IPublisher.Enqueue`).
   - One README section.

Each batch ends with a build-clean checkpoint (`dotnet build Warp.slnx`) and the relevant test slice green.

## Assumptions

- Warp Core's existing source-gen registers handlers in the consuming app's DI container, so resolving `IRequestHandler<,>` / `IPublisher` / `IMediator` from the request scope just works. Confirmed at `src/core/Warp.Core/Handlers/MediatorDispatcher.cs:39-60` and `Publisher.cs:16-54`.
- ASP.NET 9/10 host configures `JsonSerializerOptions` via `Microsoft.AspNetCore.Http.Json.JsonOptions`, which we read at request time.
- We can attach endpoint metadata for OpenAPI via `EndpointBuilder` without a hard dep on `Microsoft.AspNetCore.OpenApi`.
- The existing `Warp.SourceGenerator` ships as an analyzer DLL packaged inside `Warp.Core`'s NuGet (`<IncludeBuildOutput>false</IncludeBuildOutput>` + `analyzers/dotnet/cs/`). We mirror that for `Warp.Http.SourceGenerator` inside `Warp.Http`. Confirm the existing csproj layout in Batch 1.

## Risks

- **Source-gen complexity.** Emitting binding code per request type adds non-trivial generator logic. Mitigation: the existing `Warp.SourceGenerator` already does similar work for handler dispatch — pattern is proven. We keep emitters small and tested individually via `AttributeDiscoveryTests`.
- **Binding edge cases.** Property names that collide between route + query, complex types in query strings, nullable vs default-value semantics. Mitigation: explicit attributes win; tests cover the common matrix; document the precedence rules.
- **Pipeline behavior on HTTP path.** Existing Warp behaviors are already safe: `RetryPipelineBehavior` has `where TRequest : IRequest<TResponse>, IJob` so it never instantiates on the HTTP path (HTTP can't expose `IJob`); `MutexPipelineBehavior` runtime-checks `request is not IJob` and bypasses. User-defined behaviors that misbehave on HTTP are the user's responsibility — no HTTP-aware opt-out mechanism in v1.
- **Tight coupling to ASP.NET version.** Endpoint metadata uses `Microsoft.AspNetCore.Http.dll` from the consuming app's TFM. Mitigation: target the same `net10.0` as the rest of Warp; no support burden for downlevel TFMs.
- **Generator + analyzer interaction during build.** Source generators can interact awkwardly with StyleCop / Roslynator / Sonar (false positives on emitted code). Mitigation: emit `<auto-generated/>` headers and use the same suppression patterns the existing generator uses.
- **Group naming as silent foot-gun.** A user mistypes `Group = "publik"` — the descriptor is registered into nothing, endpoint silently absent. Mitigation: document; consider an `WarpHttpOptions.RequireAllGroupsRegistered` opt-in in v2 that throws on startup if any declared group has no `MapWarpHttp(group)` call.
- **Breaking change risk: zero.** Pure additive — no existing public surface modified.

## Public Contracts Added

- `Warp.Http.WarpHttpAttribute` (+ `WarpHttpGet`, `Post`, `Put`, `Patch`, `Delete`); applied to **handler classes**; properties `Method`, `Route`, `Group`, `Name`; `AllowMultiple = true`
- `Warp.Http.IHttpResponseShape`
- `Warp.Http.WarpHttpOptions` (placeholder; reserved for v2)
- `Warp.Http.ServiceCollectionExtensions.AddWarpHttp(...)`
- `Warp.Http.EndpointRouteBuilderExtensions.MapWarpHttp(string? group = null)`
- `Warp.Http.Discovery.WarpGeneratedHttpRegistry` (public so the generator can populate it from any user assembly; mirrors `WarpGeneratedHandlerRegistry`)
- `Warp.Http.Discovery.HttpEndpointDescriptor`, `Warp.Http.Discovery.HandlerKind`
- `Warp.Http.Dispatch.WarpHttpInvocation`, `JsonResponseWriter`, `SseResponseWriter` (generated-code-support helpers; documented as such)

**Note:** `Warp.Http.Binding.From{Route,Query,Header,Body}Attribute` were originally proposed but dropped during implementation in favor of `Microsoft.AspNetCore.Mvc.From*Attribute`. The shipped Warp.Http delegates all parameter binding to ASP.NET Minimal API, which uses the standard `Microsoft.AspNetCore.Mvc` attributes on request properties.

## Security Impact

Low. The package adds HTTP entry points but applies no implicit auth — users wire `.RequireAuthorization()` themselves. Pipeline behaviors (which may already include auth) run unchanged. JSON deserialization uses the host's `JsonSerializerOptions` (no custom converters injected). No new secrets, no new persistence, no new surface for log injection (we don't log payload bodies — §1.2). No coupling to `Warp.UI` means no risk of leaking dashboard internals through this package.
