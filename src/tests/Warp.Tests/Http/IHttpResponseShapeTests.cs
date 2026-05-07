using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Shouldly;
using Warp.Http;
using Warp.Tests.TestData;

namespace Warp.Tests.Http;

[Trait("Category", "NoDb")]
public sealed class IHttpResponseShapeTests
{
    [TimedFact]
    public async Task Apply_RunsForNonUnitRequestResponse_OverridingStatusAndHeaders()
    {
        await using var app = await WarpHttpTestApp.StartAsync(configureApp: a => a.MapWarpHttp());

        var resp = await app.Client.PostAsJsonAsync("/api/orders/created", new { CustomerName = "Alice" });

        resp.StatusCode.ShouldBe(HttpStatusCode.Created);
        resp.Headers.Location.ShouldNotBeNull();
        resp.Headers.Location!.ToString().ShouldBe("/api/orders/11111111-1111-1111-1111-111111111111");

        var body = await resp.Content.ReadFromJsonAsync<CreatedOrderResponse>();
        body.ShouldNotBeNull();
        body.CustomerName.ShouldBe("Alice");
    }

    [TimedFact]
    public async Task Apply_DoesNotRunForUnitResponses_StaysAt204()
    {
        await using var app = await WarpHttpTestApp.StartAsync(configureApp: a => a.MapWarpHttp());

        // /api/no-response uses IRequest<Unit> and writes 204 — no Apply hook ever sees a Unit value.
        var resp = await app.Client.PostAsJsonAsync("/api/no-response", new { Tag = "x" });

        resp.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        resp.Headers.Location.ShouldBeNull();
    }

    [TimedFact]
    public async Task Apply_DoesNotRunForStreamResponses_StatusStaysAt200()
    {
        await using var app = await WarpHttpTestApp.StartAsync(configureApp: a => a.MapWarpHttp());

        // SseResponseWriter doesn't invoke Apply — the stream's per-item type may itself
        // implement IHttpResponseShape but we still emit a single 200 + text/event-stream.
        var resp = await app.Client.GetAsync(new Uri("/api/stream/numbers?Count=1", UriKind.Relative));

        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
        resp.Headers.Location.ShouldBeNull();
    }
}
