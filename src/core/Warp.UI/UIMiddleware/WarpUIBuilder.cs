using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Warp.UI.DashboardPush;
using Warp.UI.Endpoints;
using Warp.UI.Extensions;

namespace Warp.UI.UIMiddleware;

public static class WarpUIBuilder
{
    /// <summary>
    /// Register the WarpUI middleware with provided options
    /// </summary>
    public static IApplicationBuilder UseWarpUI(this WebApplication app, WarpUIOptions options)
    {
        var extensions = app.Services.GetServices<IWarpUIExtension>().ToList();

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

        app.UseMiddleware<WarpUIMiddleware>(options);
        app.MapWarpApiEndpoints(options, extensions);

        if (app.Services.GetService<IDashboardPushMarker>() is not null)
        {
            app.MapHub<WarpDashboardHub>($"{options.RoutePrefix}/api/hub");
        }

        return app;
    }

    /// <summary>
    /// Register the WarpUI middleware with optional setup action for DI-injected options
    /// </summary>
    public static IApplicationBuilder UseWarpUI(this WebApplication app, Action<WarpUIOptions>? setupAction = null)
    {
        WarpUIOptions options;
        using (var scope = app.Services.CreateScope())
        {
            options = scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<WarpUIOptions>>().Value;
            setupAction?.Invoke(options);
        }

        return app.UseWarpUI(options);
    }
}
