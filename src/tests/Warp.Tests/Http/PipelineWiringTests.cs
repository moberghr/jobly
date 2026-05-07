using System.Linq;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Warp.Http;
using Warp.Http.Discovery;
using Warp.Tests.TestData;

namespace Warp.Tests.Http;

/// <summary>
/// Smoke-tests that the WarpHttpTestApp infrastructure exercises the FULL ASP.NET pipeline,
/// not a shortcut. Confirms: source generator output is reachable, MapWarpHttp populates
/// EndpointDataSource, and a real request flows through routing → endpoint dispatch →
/// IMediator pipeline → handler → JSON response.
/// </summary>
[Trait("Category", "NoDb")]
public sealed class PipelineWiringTests
{
    [TimedFact]
    public void SourceGeneratorOutput_IsLoadedAtModuleInit()
    {
        // The [ModuleInitializer] in the generated WarpHttpRegistry.g.cs runs before any user
        // code, populating WarpGeneratedHttpRegistry. If discovery weren't running at compile
        // time, this snapshot would be empty.
        var snapshot = WarpGeneratedHttpRegistry.Snapshot();

        snapshot.ShouldNotBeEmpty();
        snapshot.ShouldContain(d => d.Route == "/api/echo" && d.Method == "POST");
    }

    [TimedFact]
    public async Task MapWarpHttp_RegistersEndpointsInRealEndpointDataSource()
    {
        // ASP.NET's EndpointDataSource is the canonical place where routes live. If our
        // MapWarpHttp call wasn't going through the standard ASP.NET wiring, the routes
        // wouldn't appear here.
        await using var app = await WarpHttpTestApp.StartAsync(configureApp: a => a.MapWarpHttp());

        var dataSource = app.Services.GetRequiredService<EndpointDataSource>();
        var routePatterns = dataSource.Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText)
            .ToArray();

        routePatterns.ShouldContain("/api/echo");
        routePatterns.ShouldContain("/api/orders/{id}");
        routePatterns.ShouldContain("/api/stream/numbers");
    }

    [TimedFact]
    public async Task RealRequest_FlowsThroughAspNetRoutingAndPipelineBehaviorAndHandler()
    {
        // End-to-end probe: the test pipeline behavior records that it ran, ASP.NET dispatched
        // the request to our delegate, and the handler produced a response. If any layer were
        // skipped, one of these assertions fails.
        var probe = new PipelineProbe();

        await using var app = await WarpHttpTestApp.StartAsync(
            configureServices: s =>
            {
                s.AddSingleton(probe);
                s.AddTransient<
                    Warp.Core.Handlers.IPipelineBehavior<EchoRequest, EchoResponse>,
                    ProbeBehavior>();
            },
            configureApp: a =>
            {
                // A no-op middleware confirms ASP.NET's middleware chain is active for our routes.
                a.Use(async (ctx, next) =>
                {
                    probe.MiddlewareSawRequest = true;
                    await next();
                });
                a.MapWarpHttp();
            });

        var resp = await app.Client.PostAsJsonAsync("/api/echo", new { Text = "wired" });

        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<EchoResponse>();
        body!.Text.ShouldBe("wired");

        probe.MiddlewareSawRequest.ShouldBeTrue("ASP.NET middleware was bypassed");
        probe.PipelineBehaviorRan.ShouldBeTrue("IMediator pipeline behavior wasn't invoked");
    }

    private sealed class PipelineProbe
    {
        public bool MiddlewareSawRequest { get; set; }

        public bool PipelineBehaviorRan { get; set; }
    }

    private sealed class ProbeBehavior(PipelineProbe probe)
        : Warp.Core.Handlers.IPipelineBehavior<EchoRequest, EchoResponse>
    {
        public async Task<EchoResponse> HandleAsync(
            EchoRequest request,
            Warp.Core.Handlers.RequestHandlerDelegate<EchoRequest, EchoResponse> next,
            CancellationToken cancellationToken)
        {
            probe.PipelineBehaviorRan = true;
            return await next(request, cancellationToken).ConfigureAwait(false);
        }
    }
}
