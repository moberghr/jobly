using System.Reflection;
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

        // Handle built-in login/logout endpoints
        if (_options.CredentialValidatorType != null)
        {
            var loginPath = $"{_options.RoutePrefix}/api/auth/login";
            var logoutPath = $"{_options.RoutePrefix}/api/auth/logout";

            if (path.Equals(loginPath, StringComparison.OrdinalIgnoreCase) && string.Equals(httpContext.Request.Method, "POST", StringComparison.Ordinal))
            {
                await HandleLogin(httpContext);
                return;
            }

            if (path.Equals(logoutPath, StringComparison.OrdinalIgnoreCase) && string.Equals(httpContext.Request.Method, "POST", StringComparison.Ordinal))
            {
                httpContext.Response.Cookies.Delete(CookieName, new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Strict,
                    Secure = httpContext.Request.IsHttps,
                    Path = _options.RoutePrefix,
                });
                httpContext.Response.StatusCode = 200;
                return;
            }
        }

        // Auth check for dashboard requests
        if (path.StartsWith(_options.RoutePrefix, StringComparison.Ordinal)
            && _options.Authorization != null && !_options.Authorization.Authorize(httpContext))
        {
            // API requests always get 401 (SPA handles it)
            if (path.Contains("/api/", StringComparison.Ordinal))
            {
                httpContext.Response.StatusCode = 401;
                return;
            }

            // Built-in login: let the SPA through so it can show its own login page
            if (_options.CredentialValidatorType != null)
            {
                // Fall through to serve SPA — React app will detect 401 from API and show login
            }
            else if (_options.UnauthorizedRedirectUrl != null)
            {
                var returnUrl = Uri.EscapeDataString(path);
                httpContext.Response.Redirect($"{_options.UnauthorizedRedirectUrl}?returnUrl={returnUrl}");
                return;
            }
            else
            {
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

    private async Task HandleLogin(HttpContext httpContext)
    {
        var form = await httpContext.Request.ReadFormAsync(httpContext.RequestAborted);
        var username = form["username"].FirstOrDefault() ?? string.Empty;
        var password = form["password"].FirstOrDefault() ?? string.Empty;

        using var scope = _scopeFactory!.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<IJoblyCredentialValidator>();

        if (await validator.ValidateAsync(username, password))
        {
            var token = _protector!.Protect($"jobly|{username}|{DateTime.UtcNow:O}");
            httpContext.Response.Cookies.Append(CookieName, token, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Strict,
                Secure = httpContext.Request.IsHttps,
                Path = _options.RoutePrefix,
                Expires = DateTimeOffset.UtcNow.AddDays(1),
            });
            httpContext.Response.StatusCode = 200;
            return;
        }

        httpContext.Response.StatusCode = 401;
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
        var hasLogin = _options.CredentialValidatorType != null ? "true" : "false";
        var appSettingsString = $"<script> window.apiPath = \"{_options.RoutePrefix}/api/\"; window.basePath = \"{_options.RoutePrefix}\"; window.hasBuiltInLogin = {hasLogin};</script>";
        htmlString = htmlString.Insert(headEndIndex, appSettingsString);

        await response.WriteAsync(htmlString, Encoding.UTF8, response.HttpContext.RequestAborted);
    }

    /// <summary>
    /// Built-in auth filter that validates the Jobly cookie.
    /// </summary>
    private sealed class CookieAuthorizationFilter : IJoblyAuthorizationFilter
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
