using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.Extensions;

namespace Handfire.Core
{
	public class HandfireUIMiddleware
	{
		private readonly HandfireUIOptions _options;
		private readonly StaticFileMiddleware _staticFileMiddleware;
		private const string EmbeddedFileNamespace = "Handfire.Core.UI";
		public HandfireUIMiddleware(
			RequestDelegate next,
			IWebHostEnvironment hostingEnv,
			ILoggerFactory loggerFactory,
			HandfireUIOptions options)
		{
			_options = options ?? new HandfireUIOptions();
			_staticFileMiddleware = CreateStaticFileMiddleware(next, hostingEnv, loggerFactory, options);
		}
		public async Task Invoke(HttpContext httpContext)
		{
			var httpMethod = httpContext.Request.Method;
			var path = httpContext.Request.Path.Value;

			// If the RoutePrefix is requested (with or without trailing slash), redirect to index URL
			if (httpMethod == "GET" && Regex.IsMatch(path, $"^/?{Regex.Escape(_options.RoutePrefix)}/?$", RegexOptions.IgnoreCase))
			{
				// Use relative redirect to support proxy environments
				var relativeIndexUrl = string.IsNullOrEmpty(path) || path.EndsWith("/")
					? "index.html"
					: $"{path.Split('/').Last()}/index.html";

				RespondWithRedirect(httpContext.Response, relativeIndexUrl);
				return;
			}

			if (httpMethod == "GET" && Regex.IsMatch(path, $"^/{Regex.Escape(_options.RoutePrefix)}/?index.html$", RegexOptions.IgnoreCase))
			{
				await RespondWithIndexHtml(httpContext.Response);
				return;
			}
			await _staticFileMiddleware.Invoke(httpContext);

		}
		public static StaticFileMiddleware CreateStaticFileMiddleware(
		RequestDelegate next,
		IWebHostEnvironment hostingEnv,
		ILoggerFactory loggerFactory, HandfireUIOptions _options)
		{
			var staticFileOptions = new StaticFileOptions
			{
				RequestPath = _options.RoutePrefix,
				FileProvider = new EmbeddedFileProvider(typeof(HandfireUIMiddleware).GetTypeInfo().Assembly, EmbeddedFileNamespace),
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

			using (var stream = _options.IndexStream())
			{
				using var reader = new StreamReader(stream);

				// Inject arguments before writing to response
				var htmlBuilder = new StringBuilder(await reader.ReadToEndAsync());

				await response.WriteAsync(htmlBuilder.ToString(), Encoding.UTF8);
			}
		}

	}
}