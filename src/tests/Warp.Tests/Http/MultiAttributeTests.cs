using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Shouldly;
using Warp.Http;
using Warp.Http.Discovery;
using Warp.Tests.TestData;

namespace Warp.Tests.Http;

[Trait("Category", "NoDb")]
public sealed class MultiAttributeTests
{
    [TimedFact]
    public void Generator_EmitsOneDescriptorPerWarpHttpAttribute()
    {
        var snapshot = WarpGeneratedHttpRegistry.Snapshot();

        var multi = snapshot.Where(d => d.RequestType == typeof(MultiRouteRequest)).ToArray();
        multi.Length.ShouldBe(2);

        multi.Select(d => d.Route).ShouldBe(["/api/v1/multi", "/api/v2/multi"], ignoreOrder: true);
        multi.Select(d => d.Name).ShouldBe(["MultiV1", "MultiV2"], ignoreOrder: true);
    }

    [TimedFact]
    public async Task BothRoutes_DispatchToTheSameHandler()
    {
        await using var app = await WarpHttpTestApp.StartAsync(configureApp: a => a.MapWarpHttp());

        var v1 = await app.Client.PostAsJsonAsync("/api/v1/multi", new { Tag = "alpha" });
        var v2 = await app.Client.PostAsJsonAsync("/api/v2/multi", new { Tag = "beta" });

        v1.StatusCode.ShouldBe(HttpStatusCode.OK);
        v2.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await v1.Content.ReadFromJsonAsync<string>()).ShouldBe("got: alpha");
        (await v2.Content.ReadFromJsonAsync<string>()).ShouldBe("got: beta");
    }
}
