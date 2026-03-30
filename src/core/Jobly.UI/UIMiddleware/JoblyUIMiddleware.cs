using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobly.UI.UIMiddleware;

public class JoblyUIMiddleware
{
    private readonly RequestDelegate _next;
    private readonly JoblyUIOptions _options;
    private readonly StaticFileMiddleware _staticFileMiddleware;
    private const string EmbeddedFileNamespace = "Jobly.UI.dist";

    public JoblyUIMiddleware(RequestDelegate next, IWebHostEnvironment hostingEnv, ILoggerFactory loggerFactory, JoblyUIOptions options)
    {
        _next = next;
        _options = options ?? new JoblyUIOptions();
        _staticFileMiddleware = CreateStaticFileMiddleware(next, hostingEnv, loggerFactory, _options);
    }

    public async Task Invoke(HttpContext httpContext)
    {
        var httpMethod = httpContext.Request.Method;
        var path = httpContext.Request.Path.Value!;

        var extension = Path.GetExtension(path);

        if (!string.IsNullOrWhiteSpace(extension))
        {
            await _staticFileMiddleware.Invoke(httpContext);

            return;
        }

        if (string.Equals(httpMethod, HttpMethod.Get.Method, StringComparison.Ordinal) && path.StartsWith(_options.RoutePrefix, StringComparison.Ordinal) && !path.Contains("/api/", StringComparison.Ordinal))
        {
            await RespondWithIndexHtml(httpContext.Response);

            return;
        }

        await _next(httpContext);
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

        // Inject arguments before writing to response
        var htmlBuilder = new StringBuilder(await reader.ReadToEndAsync(response.HttpContext.RequestAborted));

        var htmlString = htmlBuilder.ToString();

        // Rewrite relative paths to use the route prefix (works for both Vite and CRA output)
        htmlString = htmlString.Replace("href=\"./", $"href=\"{_options.RoutePrefix}/", StringComparison.Ordinal);
        htmlString = htmlString.Replace("src=\"./", $"src=\"{_options.RoutePrefix}/", StringComparison.Ordinal);

        var headEndIndex = htmlString.IndexOf("</head>", StringComparison.Ordinal);

        var appSettingsString = $"<script> window.apiPath = \"{_options.RoutePrefix}/api/\"; window.basePath = \"{_options.RoutePrefix}\";</script>";
        htmlString = htmlString.Insert(headEndIndex, appSettingsString);

        await response.WriteAsync(htmlString, Encoding.UTF8, response.HttpContext.RequestAborted);
    }
}
