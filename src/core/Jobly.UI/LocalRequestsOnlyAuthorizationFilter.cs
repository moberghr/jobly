using System.Net;
using Microsoft.AspNetCore.Http;

namespace Jobly.UI;

/// <summary>
/// Allows access only from localhost (127.0.0.1 / ::1).
/// </summary>
public class LocalRequestsOnlyAuthorizationFilter : IJoblyAuthorizationFilter
{
    public bool Authorize(HttpContext httpContext)
    {
        var remoteIp = httpContext.Connection.RemoteIpAddress;
        return remoteIp != null && IPAddress.IsLoopback(remoteIp);
    }
}
