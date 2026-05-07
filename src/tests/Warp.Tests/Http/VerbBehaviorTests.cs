using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Shouldly;
using Warp.Http;
using Warp.Tests.TestData;

namespace Warp.Tests.Http;

[Trait("Category", "NoDb")]
public sealed class VerbBehaviorTests
{
    [TimedFact]
    public async Task Get_RoutesToGetEndpoint()
    {
        await using var app = await WarpHttpTestApp.StartAsync(configureApp: a => a.MapWarpHttp());

        var id = Guid.NewGuid();
        var resp = await app.Client.GetAsync(new Uri("/api/orders/" + id, UriKind.Relative));

        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [TimedFact]
    public async Task Post_RoutesToPostEndpoint()
    {
        await using var app = await WarpHttpTestApp.StartAsync(configureApp: a => a.MapWarpHttp());

        var resp = await app.Client.PostAsJsonAsync("/api/echo", new { Text = "x" });

        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [TimedFact]
    public async Task Put_RoutesToPutEndpoint()
    {
        await using var app = await WarpHttpTestApp.StartAsync(configureApp: a => a.MapWarpHttp());

        var id = Guid.NewGuid();
        var resp = await app.Client.PutAsJsonAsync("/api/products/" + id, new { Name = "n", Price = 1m });

        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [TimedFact]
    public async Task Patch_RoutesToPatchEndpoint()
    {
        await using var app = await WarpHttpTestApp.StartAsync(configureApp: a => a.MapWarpHttp());

        var id = Guid.NewGuid();
        var resp = await app.Client.PatchAsJsonAsync("/api/products/" + id + "/price", new { Price = 5m });

        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [TimedFact]
    public async Task Delete_RoutesToDeleteEndpoint()
    {
        await using var app = await WarpHttpTestApp.StartAsync(configureApp: a => a.MapWarpHttp());

        var id = Guid.NewGuid();
        var resp = await app.Client.DeleteAsync(new Uri("/api/orders/" + id, UriKind.Relative));

        resp.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [TimedFact]
    public async Task WrongVerbReturns405OrEquivalent()
    {
        await using var app = await WarpHttpTestApp.StartAsync(configureApp: a => a.MapWarpHttp());

        // /api/echo is POST-only — GET should not match.
        var resp = await app.Client.GetAsync(new Uri("/api/echo", UriKind.Relative));

        resp.StatusCode.ShouldBeOneOf(HttpStatusCode.MethodNotAllowed, HttpStatusCode.NotFound);
    }
}
