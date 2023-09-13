using Handfire.Core.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Handfire.Core
{
    public static class HandfireUIBuilder
    {
        /// <summary>
        /// Register the HandfireUI middleware with provided options
        /// </summary>
        public static IApplicationBuilder UseHandfireUI(this WebApplication app, HandfireUIOptions options)
        {
            app.UseMiddleware<HandfireUIMiddleware>(options);
            app.MapHandfireApiEndpoints(options);

            return app;
        }

        /// <summary>
        /// Register the HandfireUI middleware with optional setup action for DI-injected options
        /// </summary>
        public static IApplicationBuilder UseHandfireUI(this WebApplication app, Action<HandfireUIOptions> setupAction = null)
        {
            HandfireUIOptions options;
            using (var scope = app.Services.CreateScope())
            {
                options = scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<HandfireUIOptions>>().Value;
                setupAction?.Invoke(options);
            }

            return app.UseHandfireUI(options);
        }
    }
}
