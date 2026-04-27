using Microsoft.AspNetCore.Http;

namespace Warp.UI;

/// <summary>
/// Implement this interface to control access to the Warp dashboard and API.
/// Return true to allow access, false to deny.
/// </summary>
public interface IWarpAuthorizationFilter
{
    bool Authorize(HttpContext httpContext);
}
