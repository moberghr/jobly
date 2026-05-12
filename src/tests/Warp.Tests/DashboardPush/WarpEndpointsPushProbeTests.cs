using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Warp.UI;
using Warp.UI.DashboardPush;
using Warp.UI.UIMiddleware;
using XunitTestContext = Xunit.TestContext;

namespace Warp.Tests.DashboardPush;

/// <summary>
/// Hide-on-404 contract for the dashboard-push probe endpoint. Mirrors the pattern used
/// by <c>/api/concurrency</c>: the frontend probes once at boot and falls back to polling
/// if the endpoint is absent.
/// </summary>
[Trait("Category", "NoDb")]
public class WarpEndpointsPushProbeTests
{
    private const string ProbePath = "/warp/api/dashboard/push/probe";

    [TimedFact]
    public async Task PushProbe_Returns404_WhenAddonNotRegistered()
    {
        var (app, client) = await CreateApp(registerMarker: false);
        try
        {
            var response = await client.GetAsync(ProbePath, XunitTestContext.Current.CancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
        finally
        {
            client.Dispose();
            await app.DisposeAsync();
        }
    }

    [TimedFact]
    public async Task PushProbe_Returns200_WhenAddonRegistered()
    {
        var (app, client) = await CreateApp(registerMarker: true);
        try
        {
            var response = await client.GetAsync(ProbePath, XunitTestContext.Current.CancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);

            var body = await response.Content.ReadAsStringAsync(XunitTestContext.Current.CancellationToken);
            body.ShouldContain("\"enabled\":true");
        }
        finally
        {
            client.Dispose();
            await app.DisposeAsync();
        }
    }

    private static async Task<(WebApplication App, HttpClient Client)> CreateApp(bool registerMarker)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddDataProtection();

        if (registerMarker)
        {
            builder.Services.AddSingleton<IDashboardPushMarker, DashboardPushMarker>();
            builder.Services.AddSignalR();
        }

        var app = builder.Build();
        var options = new WarpUIOptions();
        app.UseWarpUI(options);

        await app.StartAsync(CancellationToken.None);

        return (app, app.GetTestClient());
    }
}
