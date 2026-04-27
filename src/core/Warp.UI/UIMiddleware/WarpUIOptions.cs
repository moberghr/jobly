using System.Reflection;

namespace Warp.UI.UIMiddleware;

public class WarpUIOptions
{
    public string RoutePrefix { get; set; } = "/warp";

    public Func<Stream> IndexStream { get; set; } = () => typeof(WarpUIOptions).GetTypeInfo().Assembly.GetManifestResourceStream("Warp.UI.dist.index.html")!;

    /// <summary>
    /// Authorization filter for the dashboard. Null = allow all (default).
    /// When CredentialValidator is set, this is auto-configured to check the Warp cookie.
    /// </summary>
    public IWarpAuthorizationFilter? Authorization { get; set; }

    /// <summary>
    /// URL to redirect to when unauthorized. If set, browser requests get 302 redirect
    /// with ?returnUrl= parameter. Takes precedence over the built-in login page.
    /// </summary>
    public string? UnauthorizedRedirectUrl { get; set; }

    /// <summary>
    /// Type of the IWarpCredentialValidator implementation for the built-in login page.
    /// Set via UseBuiltInLogin&lt;T&gt;(). Null = no built-in login.
    /// </summary>
    internal Type? CredentialValidatorType { get; set; }

    /// <summary>
    /// Enables the built-in login page with the specified credential validator.
    /// The validator is registered in DI as scoped, so it can inject DbContext, etc.
    /// </summary>
    public void UseBuiltInLogin<TValidator>()
        where TValidator : class, IWarpCredentialValidator
    {
        CredentialValidatorType = typeof(TValidator);
    }
}
