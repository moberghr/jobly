using Microsoft.AspNetCore.Http;

namespace Jobly.UI;

/// <summary>
/// Implement this interface to control access to the Jobly dashboard and API.
/// Return true to allow access, false to deny.
/// </summary>
public interface IJoblyAuthorizationFilter
{
    bool Authorize(HttpContext httpContext);
}
