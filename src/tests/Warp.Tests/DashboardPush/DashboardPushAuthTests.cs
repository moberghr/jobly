using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Warp.UI;
using Warp.UI.DashboardPush;
using Warp.UI.UIMiddleware;
using XunitTestContext = Xunit.TestContext;

namespace Warp.Tests.DashboardPush;

/// <summary>
/// Auth integration tests for the dashboard SignalR hub. Verifies that <see cref="WarpUIMiddleware"/>
/// gates negotiate requests the same way it gates ordinary <c>/api/</c> endpoints — no parallel
/// auth code path. Uses ASP.NET test host with no Warp services beyond the UI middleware.
/// </summary>
[Trait("Category", "NoDb")]
public class DashboardPushAuthTests
{
    private const string NegotiatePath = "/warp/api/hub/negotiate?negotiateVersion=1";

    private static async Task<(WebApplication App, HttpClient Client)> CreateApp(Action<WarpUIOptions>? configure = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddDataProtection();
        builder.Services.AddScoped<IWarpCredentialValidator, TestCredentialValidator>();
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<IDashboardPushMarker, DashboardPushMarker>();

        var options = new WarpUIOptions();
        configure?.Invoke(options);

        var app = builder.Build();
        app.UseMiddleware<WarpUIMiddleware>(options);
        app.MapHub<WarpDashboardHub>($"{options.RoutePrefix}/api/hub");

        await app.StartAsync(CancellationToken.None);

        return (app, app.GetTestClient());
    }

    [TimedFact]
    public async Task NoAuth_NegotiateReturnsOk()
    {
        var (app, client) = await CreateApp();
        try
        {
            using var content = new ByteArrayContent([]);
            var response = await client.PostAsync(NegotiatePath, content, XunitTestContext.Current.CancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        finally
        {
            client.Dispose();
            await app.DisposeAsync();
        }
    }

    [TimedFact]
    public async Task BuiltInLogin_NegotiateReturns401WithoutCookie()
    {
        var (app, client) = await CreateApp(o => o.UseBuiltInLogin<TestCredentialValidator>());
        try
        {
            using var content = new ByteArrayContent([]);
            var response = await client.PostAsync(NegotiatePath, content, XunitTestContext.Current.CancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
        finally
        {
            client.Dispose();
            await app.DisposeAsync();
        }
    }

    [TimedFact]
    public async Task BuiltInLogin_NegotiateReturns200WithCookie()
    {
        var (app, client) = await CreateApp(o => o.UseBuiltInLogin<TestCredentialValidator>());
        try
        {
            using var form = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("username", "admin"),
                new KeyValuePair<string, string>("password", "admin"),
            ]);
            var loginResponse = await client.PostAsync("/warp/api/auth/login", form, XunitTestContext.Current.CancellationToken);
            loginResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

            var setCookie = loginResponse.Headers.GetValues("Set-Cookie").First();
            var cookieValue = setCookie.Split(';')[0];
            client.DefaultRequestHeaders.Add("Cookie", cookieValue);

            using var content = new ByteArrayContent([]);
            var response = await client.PostAsync(NegotiatePath, content, XunitTestContext.Current.CancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        finally
        {
            client.Dispose();
            await app.DisposeAsync();
        }
    }

    [TimedFact]
    public async Task CustomFilterDeny_NegotiateReturns401()
    {
        var (app, client) = await CreateApp(o => o.Authorization = new DenyAllFilter());
        try
        {
            using var content = new ByteArrayContent([]);
            var response = await client.PostAsync(NegotiatePath, content, XunitTestContext.Current.CancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
        finally
        {
            client.Dispose();
            await app.DisposeAsync();
        }
    }

    private sealed class TestCredentialValidator : IWarpCredentialValidator
    {
        public Task<bool> ValidateAsync(string username, string password)
            => Task.FromResult(
                string.Equals(username, "admin", StringComparison.Ordinal)
                && string.Equals(password, "admin", StringComparison.Ordinal));
    }

    private sealed class DenyAllFilter : IWarpAuthorizationFilter
    {
        public bool Authorize(HttpContext httpContext) => false;
    }
}
