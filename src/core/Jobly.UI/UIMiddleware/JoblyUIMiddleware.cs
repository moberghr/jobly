using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobly.UI.UIMiddleware
{
    public class JoblyUIMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly JoblyUIOptions _options;
        private readonly StaticFileMiddleware _staticFileMiddleware;
        private const string EmbeddedFileNamespace = "Jobly.Core.UI";

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

            if (httpMethod == HttpMethod.Get.Method && path.StartsWith(_options.RoutePrefix) && !path.Contains("/api/"))
            {
                await RespondWithIndexHtml(httpContext.Response);

                return;
            }

            //if (httpMethod == "GET" && Regex.IsMatch(path, $"^/?{Regex.Escape(_options.RoutePrefix)}/?$", RegexOptions.IgnoreCase))
            //{
            //    // Use relative redirect to support proxy environments
            //    var relativeIndexUrl = string.IsNullOrEmpty(path) || path.EndsWith("/")
            //        ? "index.html"
            //        : $"{path.Split('/').Last()}/index.html";

            //    RespondWithRedirect(httpContext.Response, relativeIndexUrl);

            //    return;
            //}

            //if (httpMethod == "GET" && Regex.IsMatch(path, $"^/{Regex.Escape(_options.RoutePrefix)}/?index.html$", RegexOptions.IgnoreCase))
            //{
            //    await RespondWithIndexHtml(httpContext.Response);

            //    return;
            //}

            await _next(httpContext);
        }

        public static StaticFileMiddleware CreateStaticFileMiddleware(RequestDelegate next, IWebHostEnvironment hostingEnv, ILoggerFactory loggerFactory, JoblyUIOptions _options)
        {
            var staticFileOptions = new StaticFileOptions
            {
                RequestPath = _options.RoutePrefix,
                FileProvider = new EmbeddedFileProvider(typeof(JoblyUIMiddleware).GetTypeInfo().Assembly, EmbeddedFileNamespace),
            };

            return new StaticFileMiddleware(next, hostingEnv, Options.Create(staticFileOptions), loggerFactory);
        }

        private void RespondWithRedirect(HttpResponse response, string location)
        {
            response.StatusCode = 301;
            response.Headers["Location"] = location;
        }

        private async Task RespondWithIndexHtml(HttpResponse response)
        {
            response.StatusCode = 200;
            response.ContentType = "text/html;charset=utf-8";

            using var stream = _options.IndexStream();
            using var reader = new StreamReader(stream);

            // Inject arguments before writing to response
            var htmlBuilder = new StringBuilder(await reader.ReadToEndAsync());

            var htmlString = htmlBuilder.ToString();

            htmlString = htmlString.Replace("href=\"static", $"href=\"{_options.RoutePrefix}/static");
            htmlString = htmlString.Replace("href=\"favicon", $"href=\"{_options.RoutePrefix}/favicon");
            htmlString = htmlString.Replace("src=\"static", $"src=\"{_options.RoutePrefix}/static");

            var headEndIndex = htmlString.IndexOf("</head>");

            var appSettingsString = $"<script> window.apiPath = \"{_options.RoutePrefix}/api/\";</script>";
            htmlString = htmlString.Insert(headEndIndex, appSettingsString);

            await response.WriteAsync(htmlString, Encoding.UTF8);
        }

    }
}