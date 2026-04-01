using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobly.UI.UIMiddleware;

public class JoblyUIMiddleware
{
    private readonly RequestDelegate _next;
    private readonly JoblyUIOptions _options;
    private readonly StaticFileMiddleware _staticFileMiddleware;
    private readonly IDataProtector? _protector;
    private readonly IServiceScopeFactory? _scopeFactory;
    private const string EmbeddedFileNamespace = "Jobly.UI.dist";
    private const string CookieName = ".Jobly.Auth";

    public JoblyUIMiddleware(RequestDelegate next, IWebHostEnvironment hostingEnv, ILoggerFactory loggerFactory, IServiceProvider serviceProvider, JoblyUIOptions options)
    {
        _next = next;
        _options = options ?? new JoblyUIOptions();
        _staticFileMiddleware = CreateStaticFileMiddleware(next, hostingEnv, loggerFactory, _options);

        if (_options.CredentialValidatorType != null)
        {
            _protector = serviceProvider.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("Jobly.Auth");
            _scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

            _options.Authorization ??= new CookieAuthorizationFilter(_protector);
        }
    }

    public async Task Invoke(HttpContext httpContext)
    {
        var path = httpContext.Request.Path.Value!;

        // Handle built-in login page (always available when CredentialValidator is set)
        if (_options.CredentialValidatorType != null)
        {
            var loginPath = $"{_options.RoutePrefix}/login";
            var logoutPath = $"{_options.RoutePrefix}/logout";

            if (path.Equals(loginPath, StringComparison.OrdinalIgnoreCase))
            {
                if (httpContext.Request.Method == "GET")
                {
                    await ServeLoginPage(httpContext);
                    return;
                }

                if (httpContext.Request.Method == "POST")
                {
                    await HandleLogin(httpContext);
                    return;
                }
            }

            if (path.Equals(logoutPath, StringComparison.OrdinalIgnoreCase) && httpContext.Request.Method == "POST")
            {
                httpContext.Response.Cookies.Delete(CookieName);
                httpContext.Response.Redirect(loginPath);
                return;
            }
        }

        // Auth check for dashboard requests
        if (path.StartsWith(_options.RoutePrefix, StringComparison.Ordinal))
        {
            if (_options.Authorization != null && !_options.Authorization.Authorize(httpContext))
            {
                if (!path.Contains("/api/", StringComparison.Ordinal))
                {
                    var returnUrl = Uri.EscapeDataString(path);
                    var redirectTo = _options.UnauthorizedRedirectUrl
                        ?? (_options.CredentialValidatorType != null ? $"{_options.RoutePrefix}/login" : null);

                    if (redirectTo != null)
                    {
                        httpContext.Response.Redirect($"{redirectTo}?returnUrl={returnUrl}");
                        return;
                    }
                }

                httpContext.Response.StatusCode = 401;
                return;
            }
        }

        var extension = Path.GetExtension(path);

        if (!string.IsNullOrWhiteSpace(extension))
        {
            await _staticFileMiddleware.Invoke(httpContext);
            return;
        }

        if (string.Equals(httpContext.Request.Method, HttpMethod.Get.Method, StringComparison.Ordinal) && path.StartsWith(_options.RoutePrefix, StringComparison.Ordinal) && !path.Contains("/api/", StringComparison.Ordinal))
        {
            await RespondWithIndexHtml(httpContext.Response);
            return;
        }

        await _next(httpContext);
    }

    private async Task ServeLoginPage(HttpContext httpContext)
    {
        var returnUrl = httpContext.Request.Query["returnUrl"].FirstOrDefault() ?? _options.RoutePrefix;
        var error = httpContext.Request.Query["error"].FirstOrDefault();

        httpContext.Response.StatusCode = 200;
        httpContext.Response.ContentType = "text/html;charset=utf-8";
        await httpContext.Response.WriteAsync($$"""
            <!DOCTYPE html>
            <html>
            <head><title>Jobly Login</title>
            <style>
                body { font-family: system-ui, -apple-system, sans-serif; display: flex; justify-content: center; align-items: center; min-height: 100vh; margin: 0; background: #f5f5f5; }
                @media (prefers-color-scheme: dark) { body { background: #09090b; } .card { background: #18181b; color: #fafafa; } input { background: #27272a; border-color: #3f3f46; color: #fafafa; } .error { color: #f87171; } }
                .card { background: white; padding: 2rem; border-radius: 8px; box-shadow: 0 2px 8px rgba(0,0,0,0.1); width: 320px; }
                h1 { font-size: 1.25rem; margin: 0 0 1.5rem; }
                label { font-size: 0.875rem; display: block; margin-bottom: 0.25rem; }
                input { width: 100%; padding: 0.5rem; margin-bottom: 1rem; border: 1px solid #ddd; border-radius: 4px; box-sizing: border-box; font-size: 0.875rem; }
                button { width: 100%; padding: 0.5rem; background: #18181b; color: white; border: none; border-radius: 4px; cursor: pointer; font-size: 0.875rem; }
                button:hover { background: #27272a; }
                .error { color: #ef4444; font-size: 0.875rem; margin-bottom: 1rem; }
            </style></head>
            <body>
            <div class="card">
                <h1>Jobly Dashboard</h1>
                {{(error != null ? "<div class=\"error\">Invalid credentials</div>" : "")}}
                <form method="POST" action="{{_options.RoutePrefix}}/login?returnUrl={{Uri.EscapeDataString(returnUrl)}}">
                    <label>Username</label>
                    <input type="text" name="username" autofocus />
                    <label>Password</label>
                    <input type="password" name="password" />
                    <button type="submit">Sign in</button>
                </form>
            </div>
            </body></html>
            """);
    }

    private async Task HandleLogin(HttpContext httpContext)
    {
        var form = await httpContext.Request.ReadFormAsync();
        var username = form["username"].FirstOrDefault() ?? "";
        var password = form["password"].FirstOrDefault() ?? "";
        var returnUrl = httpContext.Request.Query["returnUrl"].FirstOrDefault() ?? _options.RoutePrefix;

        using var scope = _scopeFactory!.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<IJoblyCredentialValidator>();

        if (validator.Validate(username, password))
        {
            var token = _protector!.Protect($"jobly|{username}|{DateTime.UtcNow:O}");
            httpContext.Response.Cookies.Append(CookieName, token, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Strict,
                Secure = httpContext.Request.IsHttps,
                Path = _options.RoutePrefix,
            });
            httpContext.Response.Redirect(returnUrl);
            return;
        }

        httpContext.Response.Redirect($"{_options.RoutePrefix}/login?returnUrl={Uri.EscapeDataString(returnUrl)}&error=1");
    }

    public static StaticFileMiddleware CreateStaticFileMiddleware(RequestDelegate next, IWebHostEnvironment hostingEnv, ILoggerFactory loggerFactory, JoblyUIOptions options)
    {
        var staticFileOptions = new StaticFileOptions
        {
            RequestPath = options.RoutePrefix,
            FileProvider = new EmbeddedFileProvider(typeof(JoblyUIMiddleware).GetTypeInfo().Assembly, EmbeddedFileNamespace),
        };

        return new StaticFileMiddleware(next, hostingEnv, Options.Create(staticFileOptions), loggerFactory);
    }

    private async Task RespondWithIndexHtml(HttpResponse response)
    {
        response.StatusCode = 200;
        response.ContentType = "text/html;charset=utf-8";
        await using var stream = _options.IndexStream();
        using var reader = new StreamReader(stream);

        var htmlBuilder = new StringBuilder(await reader.ReadToEndAsync(response.HttpContext.RequestAborted));
        var htmlString = htmlBuilder.ToString();

        htmlString = htmlString.Replace("href=\"./", $"href=\"{_options.RoutePrefix}/", StringComparison.Ordinal);
        htmlString = htmlString.Replace("src=\"./", $"src=\"{_options.RoutePrefix}/", StringComparison.Ordinal);

        var headEndIndex = htmlString.IndexOf("</head>", StringComparison.Ordinal);
        var appSettingsString = $"<script> window.apiPath = \"{_options.RoutePrefix}/api/\"; window.basePath = \"{_options.RoutePrefix}\";</script>";
        htmlString = htmlString.Insert(headEndIndex, appSettingsString);

        await response.WriteAsync(htmlString, Encoding.UTF8, response.HttpContext.RequestAborted);
    }

    /// <summary>
    /// Built-in auth filter that validates the Jobly cookie.
    /// </summary>
    private class CookieAuthorizationFilter : IJoblyAuthorizationFilter
    {
        private readonly IDataProtector _protector;

        public CookieAuthorizationFilter(IDataProtector protector) => _protector = protector;

        public bool Authorize(HttpContext httpContext)
        {
            var cookie = httpContext.Request.Cookies[CookieName];
            if (string.IsNullOrEmpty(cookie))
            {
                return false;
            }

            try
            {
                var payload = _protector.Unprotect(cookie);
                return payload.StartsWith("jobly|", StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }
    }
}
