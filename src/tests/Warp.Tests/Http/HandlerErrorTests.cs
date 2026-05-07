using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Shouldly;
using Warp.Http;
using Warp.Tests.TestData;

namespace Warp.Tests.Http;

[Trait("Category", "NoDb")]
public sealed class HandlerErrorTests
{
    [TimedFact]
    public async Task HandlerThrows_PropagatesAsHandlerExceptionToClient()
    {
        await using var app = await WarpHttpTestApp.StartAsync(configureApp: a => a.MapWarpHttp());

        // The TestServer surfaces the handler's InvalidOperationException directly through
        // HttpClient (no exception middleware registered → no 5xx translation).
        var thrown = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await app.Client.PostAsJsonAsync("/api/throws", new { Marker = "fail" }));

        thrown.Message.ShouldContain("boom: fail");
    }

    [TimedFact]
    public async Task HandlerCancellation_PropagatesAsCancelledOrServerError()
    {
        await using var app = await WarpHttpTestApp.StartAsync(configureApp: a => a.MapWarpHttp());

        // The handler throws OperationCanceledException with the request-aborted token's identity.
        // TestServer surfaces it to HttpClient — never as a 200 + stale body.
        var thrown = await Should.ThrowAsync<Exception>(async () =>
            await app.Client.PostAsJsonAsync("/api/cancels", new { Marker = "x" }));

        // Either OperationCanceledException directly, or wrapped — both signal cancellation.
        (thrown is OperationCanceledException || thrown.InnerException is OperationCanceledException)
            .ShouldBeTrue($"Expected cancellation-shaped exception, got {thrown.GetType().Name}: {thrown.Message}");
    }
}
