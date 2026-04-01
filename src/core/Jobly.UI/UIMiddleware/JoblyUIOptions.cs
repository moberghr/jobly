using System.Reflection;

namespace Jobly.UI.UIMiddleware;

public class JoblyUIOptions
{
    public string RoutePrefix { get; set; } = "/jobly";

    public Func<Stream> IndexStream { get; set; } = () => typeof(JoblyUIOptions).GetTypeInfo().Assembly.GetManifestResourceStream("Jobly.UI.dist.index.html")!;

    /// <summary>
    /// Authorization filter for the dashboard. Null = allow all (default).
    /// </summary>
    public IJoblyAuthorizationFilter? Authorization { get; set; }

    /// <summary>
    /// URL to redirect to when unauthorized. If set, browser requests get 302 redirect
    /// with ?returnUrl= parameter. If null, returns 401. API requests always get 401.
    /// </summary>
    public string? UnauthorizedRedirectUrl { get; set; }
}
