using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Warp.UI;
using Warp.UI.UIMiddleware;

namespace Warp.Tests.Admin;

/// <summary>
/// Tests for dashboard authorization middleware. Uses a minimal app with no Warp services —
/// only tests the auth layer (login, logout, cookie, filter, redirect).
/// </summary>
[Trait("Category", "NoDb")]
public class DashboardAuthTests
{
    private static async Task<(WebApplication App, HttpClient Client)> CreateApp(Action<WarpUIOptions>? configure = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddDataProtection();
        builder.Services.AddScoped<IWarpCredentialValidator, TestCredentialValidator>();

        var options = new WarpUIOptions();
        configure?.Invoke(options);

        var app = builder.Build();

        // Register test API endpoint
        app.MapGet("/warp/api/test", () => Results.Ok("ok"));

        // Apply middleware manually
        app.UseMiddleware<WarpUIMiddleware>(options);

        await app.StartAsync(CancellationToken.None);
        return (app, app.GetTestClient());
    }

    [TimedFact]
    public async Task NoAuth_ApiReturnsOk()
    {
        var (app, client) = await CreateApp();
        try
        {
            var response = await client.GetAsync("/warp/api/test", CancellationToken.None);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        finally
        {
            client.Dispose();
            await app.DisposeAsync();
        }
    }

    [TimedFact]
    public async Task BuiltInLogin_ApiReturns401WithoutCookie()
    {
        var (app, client) = await CreateApp(o => o.UseBuiltInLogin<TestCredentialValidator>());
        try
        {
            var response = await client.GetAsync("/warp/api/test", CancellationToken.None);
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
        finally
        {
            client.Dispose();
            await app.DisposeAsync();
        }
    }

    [TimedFact]
    public async Task BuiltInLogin_ValidCredentials_Returns200AndSetsCookie()
    {
        var (app, client) = await CreateApp(o => o.UseBuiltInLogin<TestCredentialValidator>());
        try
        {
            var form = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("username", "admin"),
                new KeyValuePair<string, string>("password", "admin"),
            ]);
            var response = await client.PostAsync("/warp/api/auth/login", form, CancellationToken.None);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            response.Headers.TryGetValues("Set-Cookie", out var cookies).ShouldBeTrue();
            cookies.ShouldContain(c => c.Contains(".Warp.Auth"));
        }
        finally
        {
            client.Dispose();
            await app.DisposeAsync();
        }
    }

    [TimedFact]
    public async Task BuiltInLogin_InvalidCredentials_Returns401()
    {
        var (app, client) = await CreateApp(o => o.UseBuiltInLogin<TestCredentialValidator>());
        try
        {
            var form = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("username", "admin"),
                new KeyValuePair<string, string>("password", "wrong"),
            ]);
            var response = await client.PostAsync("/warp/api/auth/login", form, CancellationToken.None);

            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
        finally
        {
            client.Dispose();
            await app.DisposeAsync();
        }
    }

    [TimedFact]
    public async Task BuiltInLogin_WithCookie_ApiReturnsOk()
    {
        var (app, client) = await CreateApp(o => o.UseBuiltInLogin<TestCredentialValidator>());
        try
        {
            // Login
            var form = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("username", "admin"),
                new KeyValuePair<string, string>("password", "admin"),
            ]);
            var loginResponse = await client.PostAsync("/warp/api/auth/login", form, CancellationToken.None);
            loginResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

            var setCookie = loginResponse.Headers.GetValues("Set-Cookie").First();
            var cookieValue = setCookie.Split(';')[0];
            client.DefaultRequestHeaders.Add("Cookie", cookieValue);

            // API should work now
            var response = await client.GetAsync("/warp/api/test", CancellationToken.None);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        finally
        {
            client.Dispose();
            await app.DisposeAsync();
        }
    }

    [TimedFact]
    public async Task AuthStatus_NoAuth_ReturnsAuthenticatedTrue()
    {
        var (app, client) = await CreateApp();
        try
        {
            var response = await client.GetAsync("/warp/api/auth/status", CancellationToken.None);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
            body.ShouldBe("{\"authenticated\":true}");
        }
        finally
        {
            client.Dispose();
            await app.DisposeAsync();
        }
    }

    [TimedFact]
    public async Task AuthStatus_BuiltInLogin_NoCookie_ReturnsAuthenticatedFalse()
    {
        var (app, client) = await CreateApp(o => o.UseBuiltInLogin<TestCredentialValidator>());
        try
        {
            var response = await client.GetAsync("/warp/api/auth/status", CancellationToken.None);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
            body.ShouldBe("{\"authenticated\":false}");
        }
        finally
        {
            client.Dispose();
            await app.DisposeAsync();
        }
    }

    [TimedFact]
    public async Task AuthStatus_BuiltInLogin_WithCookie_ReturnsAuthenticatedTrue()
    {
        var (app, client) = await CreateApp(o => o.UseBuiltInLogin<TestCredentialValidator>());
        try
        {
            var form = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("username", "admin"),
                new KeyValuePair<string, string>("password", "admin"),
            ]);
            var loginResponse = await client.PostAsync("/warp/api/auth/login", form, CancellationToken.None);
            loginResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

            var setCookie = loginResponse.Headers.GetValues("Set-Cookie").First();
            var cookieValue = setCookie.Split(';')[0];
            client.DefaultRequestHeaders.Add("Cookie", cookieValue);

            var response = await client.GetAsync("/warp/api/auth/status", CancellationToken.None);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
            body.ShouldBe("{\"authenticated\":true}");
        }
        finally
        {
            client.Dispose();
            await app.DisposeAsync();
        }
    }

    [TimedFact]
    public async Task CustomAuthFilter_Unauthorized_Returns401ForApi()
    {
        var (app, client) = await CreateApp(o => o.Authorization = new DenyAllFilter());
        try
        {
            var response = await client.GetAsync("/warp/api/test", CancellationToken.None);
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
        finally
        {
            client.Dispose();
            await app.DisposeAsync();
        }
    }

    [TimedFact]
    public async Task CustomAuthFilter_WithRedirectUrl_RedirectsForSpa()
    {
        var (app, client) = await CreateApp(o =>
        {
            o.Authorization = new DenyAllFilter();
            o.UnauthorizedRedirectUrl = "/login";
        });
        try
        {
            var handler = app.GetTestServer().CreateHandler();
            using var noRedirectClient = new HttpClient(handler) { BaseAddress = client.BaseAddress };

            var response = await noRedirectClient.GetAsync("/warp", CancellationToken.None);
            response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            response.Headers.Location!.ToString().ShouldStartWith("/login");
        }
        finally
        {
            client.Dispose();
            await app.DisposeAsync();
        }
    }
}

internal class TestCredentialValidator : IWarpCredentialValidator
{
    public Task<bool> ValidateAsync(string username, string password)
    {
        return Task.FromResult(string.Equals(username, "admin", StringComparison.Ordinal) && string.Equals(password, "admin", StringComparison.Ordinal));
    }
}

internal class DenyAllFilter : IWarpAuthorizationFilter
{
    public bool Authorize(HttpContext httpContext) => false;
}
