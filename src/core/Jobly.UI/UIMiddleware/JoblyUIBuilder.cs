using Jobly.UI.Endpoints;
using Jobly.UI.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace Jobly.UI.UIMiddleware;

public static class JoblyUIBuilder
{
    /// <summary>
    /// Register the JoblyUI middleware with provided options
    /// </summary>
    public static IApplicationBuilder UseJoblyUI(this WebApplication app, JoblyUIOptions options)
    {
        var extensions = app.Services.GetServices<IJoblyUIExtension>().ToList();

        // Serve each extension's embedded JS files at /_ext/{name}/
        foreach (var ext in extensions)
        {
            var fileProvider = new EmbeddedFileProvider(ext.ResourceAssembly, ext.ResourceNamespace);
            app.UseStaticFiles(new StaticFileOptions
            {
                RequestPath = $"{options.RoutePrefix}/_ext/{ext.Name}",
                FileProvider = fileProvider,
            });
        }

        app.UseMiddleware<JoblyUIMiddleware>(options);
        app.MapJoblyApiEndpoints(options, extensions);

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
