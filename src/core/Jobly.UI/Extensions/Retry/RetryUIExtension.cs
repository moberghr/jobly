using System.Reflection;
using Microsoft.AspNetCore.Routing;

namespace Jobly.UI.Extensions.Retry;

/// <summary>
/// Built-in UI extension for the Retry addon.
/// Shows retry configuration and status on the job detail page.
/// </summary>
public class RetryUIExtension : IJoblyUIExtension
{
    public string Name => "retry";

    public Assembly ResourceAssembly => typeof(RetryUIExtension).Assembly;

    public string ResourceNamespace => "Jobly.UI.Extensions.Retry.dist";

    public UIExtensionManifest GetManifest()
    {
        return new UIExtensionManifest
        {
            Name = Name,
            ScriptUrl = $"/_ext/{Name}/index.js",
        };
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // No custom endpoints needed — retry data comes from job metadata
        // via the existing GET /detail/{id} endpoint.
    }
}
