using System.Reflection;

namespace Jobly.UI.UIMiddleware;

public class JoblyUIOptions
{
    public string RoutePrefix { get; set; } = "/jobly";

    public Func<Stream> IndexStream { get; set; } = () => typeof(JoblyUIOptions).GetTypeInfo().Assembly.GetManifestResourceStream("Jobly.UI.dist.index.html")!;

    /// <summary>
    /// Authorization filter for the dashboard. Null = allow all (default).
    /// When CredentialValidator is set, this is auto-configured to check the Jobly cookie.
    /// </summary>
    public IJoblyAuthorizationFilter? Authorization { get; set; }

    /// <summary>
    /// URL to redirect to when unauthorized. If set, browser requests get 302 redirect
    /// with ?returnUrl= parameter. Takes precedence over the built-in login page.
    /// </summary>
    public string? UnauthorizedRedirectUrl { get; set; }

    /// <summary>
    /// When true, enables the built-in login page at {RoutePrefix}/login with HTTP-only cookie auth.
    /// Register IJoblyCredentialValidator in DI to validate credentials.
    /// </summary>
    public bool UseBuiltInLogin { get; set; }
}
