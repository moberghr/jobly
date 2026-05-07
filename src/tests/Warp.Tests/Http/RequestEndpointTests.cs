using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Shouldly;
using Warp.Http;
using Warp.Tests.TestData;

namespace Warp.Tests.Http;

[Trait("Category", "NoDb")]
public sealed class RequestEndpointTests
{
    [TimedFact]
    public async Task PostBodyRequest_ReturnsHandlerResultAsJsonWith200()
    {
        await using var app = await WarpHttpTestApp.StartAsync(configureApp: a => a.MapWarpHttp());

        var resp = await app.Client.PostAsJsonAsync(
            "/api/echo",
            new { Text = "hello" });

        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<EchoResponse>();
        body.ShouldNotBeNull();
        body.Text.ShouldBe("hello");
    }

    [TimedFact]
    public async Task PostBodyRequest_WithUnitResponse_Returns204AndEmptyBody()
    {
        await using var app = await WarpHttpTestApp.StartAsync(configureApp: a => a.MapWarpHttp());

        var resp = await app.Client.PostAsJsonAsync(
            "/api/no-response",
            new { Tag = "anything" });

        resp.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        bytes.Length.ShouldBe(0);
    }

    [TimedFact]
    public async Task GetWithRouteToken_BindsRouteValueIntoRequestRecord()
    {
        await using var app = await WarpHttpTestApp.StartAsync(configureApp: a => a.MapWarpHttp());

        var id = Guid.NewGuid();
        var resp = await app.Client.GetAsync(new Uri("/api/orders/" + id, UriKind.Relative));

        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<OrderDto>();
        body.ShouldNotBeNull();
        body.Id.ShouldBe(id);
    }

    [TimedFact]
    public async Task GetWithQueryString_BindsAllParametersByName()
    {
        await using var app = await WarpHttpTestApp.StartAsync(configureApp: a => a.MapWarpHttp());

        var resp = await app.Client.GetAsync(new Uri("/api/orders?Page=3&PageSize=42", UriKind.Relative));

        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<ListOrdersResponse>();
        body.ShouldNotBeNull();
        body.Page.ShouldBe(3);
        body.PageSize.ShouldBe(42);
    }

    [TimedFact]
    public async Task PutWithRouteAndBody_BindsRouteIntoPropertyAndBodyIntoOthers()
    {
        await using var app = await WarpHttpTestApp.StartAsync(configureApp: a => a.MapWarpHttp());

        var id = Guid.NewGuid();
        var resp = await app.Client.PutAsJsonAsync(
            "/api/products/" + id,
            new { Name = "widget", Price = 9.99m });

        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<ProductDto>();
        body.ShouldNotBeNull();
        body.Id.ShouldBe(id);
        body.Name.ShouldBe("widget");
        body.Price.ShouldBe(9.99m);
    }
}
