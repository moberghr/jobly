using Jobly.UI.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Jobly.UI.UIMiddleware;

public static class JoblyUIBuilder
{
    /// <summary>
    /// Register the JoblyUI middleware with provided options
    /// </summary>
    public static IApplicationBuilder UseJoblyUI(this WebApplication app, JoblyUIOptions options)
    {
        app.UseMiddleware<JoblyUIMiddleware>(options);
        app.MapJoblyApiEndpoints(options);

        return app;
    }

    /// <summary>
    /// Register the JoblyUI middleware with optional setup action for DI-injected options
    /// </summary>
    public static IApplicationBuilder UseJoblyUI(this WebApplication app, Action<JoblyUIOptions>? setupAction = null)
    {
        JoblyUIOptions options;
        using (var scope = app.Services.CreateScope())
        {
            options = scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<JoblyUIOptions>>().Value;
            setupAction?.Invoke(options);
        }

        return app.UseJoblyUI(options);
    }
}
