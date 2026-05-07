using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Shouldly;
using Warp.Http;
using Warp.Tests.TestData;

namespace Warp.Tests.Http;

[Trait("Category", "NoDb")]
public sealed class GroupRegistrationTests
{
    [TimedFact]
    public async Task NullGroupCall_DoesNotRegisterNamedGroupEndpoints()
    {
        await using var app = await WarpHttpTestApp.StartAsync(configureApp: a => a.MapWarpHttp());

        var resp = await app.Client.GetAsync(new Uri("/group-public/ping", UriKind.Relative));

        resp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [TimedFact]
    public async Task NamedGroupCall_RegistersOnlyMatchingDescriptors()
    {
        await using var app = await WarpHttpTestApp.StartAsync(configureApp: a => a.MapWarpHttp("public"));

        var publicResp = await app.Client.GetAsync(new Uri("/group-public/ping", UriKind.Relative));
        var adminResp = await app.Client.GetAsync(new Uri("/group-admin/ping", UriKind.Relative));

        publicResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        adminResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [TimedFact]
    public async Task SeparateGroupCalls_DoNotOverlap()
    {
        await using var app = await WarpHttpTestApp.StartAsync(configureApp: a =>
        {
            a.MapWarpHttp("public");
            a.MapWarpHttp("admin");
        });

        var publicResp = await app.Client.GetAsync(new Uri("/group-public/ping", UriKind.Relative));
        var adminResp = await app.Client.GetAsync(new Uri("/group-admin/ping", UriKind.Relative));

        publicResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        adminResp.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [TimedFact]
    public async Task DoubleCall_SameBuilderSameGroup_Throws()
    {
        var thrown = await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await using var app = await WarpHttpTestApp.StartAsync(configureApp: a =>
            {
                a.MapWarpHttp("public");
                a.MapWarpHttp("public");
            });
        });

        thrown.Message.ShouldContain("public");
    }
}
