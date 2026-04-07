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
    /// Type of the IJoblyCredentialValidator implementation for the built-in login page.
    /// Set via UseBuiltInLogin&lt;T&gt;(). Null = no built-in login.
    /// </summary>
    internal Type? CredentialValidatorType { get; set; }

    /// <summary>
    /// Enables the built-in login page with the specified credential validator.
    /// The validator is registered in DI as scoped, so it can inject DbContext, etc.
    /// </summary>
    public void UseBuiltInLogin<TValidator>()
        where TValidator : class, IJoblyCredentialValidator
    {
        CredentialValidatorType = typeof(TValidator);
    }
}
