using EFCore.NamingConventions.Internal;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Handfire.Core
{
	public static class HandfireUIBuilder
	{
		/// <summary>
		/// Register the HandfireUI middleware with provided options
		/// </summary>
		public static IApplicationBuilder UseHandfireUI(this IApplicationBuilder app, HandfireUIOptions options)
		{
			return app.UseMiddleware<HandfireUIMiddleware>(options);
		}

		/// <summary>
		/// Register the HandfireUI middleware with optional setup action for DI-injected options
		/// </summary>
		public static IApplicationBuilder UseHandfireUI(
			this IApplicationBuilder app,
			Action<HandfireUIOptions> setupAction = null)
		{
			HandfireUIOptions options;
			using (var scope = app.ApplicationServices.CreateScope())
			{
				options = scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<HandfireUIOptions>>().Value;
				setupAction?.Invoke(options);
			}
			return app.UseHandfireUI(options);
		}
	}
}
