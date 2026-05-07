using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Warp.Core.Handlers;
using Warp.Http;
using Warp.Tests.TestData;

namespace Warp.Tests.Http;

[Trait("Category", "NoDb")]
public sealed class PipelineBehaviorTests
{
    [TimedFact]
    public async Task CustomPipelineBehavior_RunsAroundHandlerOnHttpPath()
    {
        var marker = new BehaviorMarker();
        await using var app = await WarpHttpTestApp.StartAsync(
            configureServices: s =>
            {
                s.AddSingleton(marker);
                s.AddTransient<IPipelineBehavior<EchoRequest, EchoResponse>, MarkerBehavior<EchoRequest, EchoResponse>>();
            },
            configureApp: a => a.MapWarpHttp());

        var resp = await app.Client.PostAsJsonAsync("/api/echo", new { Text = "hi" });

        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
        marker.RanBefore.ShouldBeTrue();
        marker.RanAfter.ShouldBeTrue();
    }

    [TimedFact]
    public async Task PipelineBehavior_CanShortCircuitWithCustomResponse()
    {
        await using var app = await WarpHttpTestApp.StartAsync(
            configureServices: s => s.AddTransient<IPipelineBehavior<EchoRequest, EchoResponse>, ShortCircuitingBehavior>(),
            configureApp: a => a.MapWarpHttp());

        var resp = await app.Client.PostAsJsonAsync("/api/echo", new { Text = "blocked" });

        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<EchoResponse>();
        body!.Text.ShouldBe("short-circuited");
    }

    public sealed class BehaviorMarker
    {
        public bool RanBefore { get; set; }

        public bool RanAfter { get; set; }
    }

    public sealed class MarkerBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        private readonly BehaviorMarker _marker;

        public MarkerBehavior(BehaviorMarker marker)
        {
            _marker = marker;
        }

        public async Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TRequest, TResponse> next, CancellationToken cancellationToken)
        {
            _marker.RanBefore = true;
            var result = await next(request, cancellationToken).ConfigureAwait(false);
            _marker.RanAfter = true;
            return result;
        }
    }

    public sealed class ShortCircuitingBehavior : IPipelineBehavior<EchoRequest, EchoResponse>
    {
        public Task<EchoResponse> HandleAsync(EchoRequest request, RequestHandlerDelegate<EchoRequest, EchoResponse> next, CancellationToken cancellationToken)
        {
            return Task.FromResult(new EchoResponse("short-circuited", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        }
    }
}
