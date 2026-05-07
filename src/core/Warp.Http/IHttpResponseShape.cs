using Microsoft.AspNetCore.Http;

namespace Warp.Http;

/// <summary>
/// Implemented by response types that want to customize the HTTP response (status,
/// headers, Location, etc.) without depending on ASP.NET in the handler. Mirrors
/// Wolverine's <c>IHttpAware</c>: the handler returns a plain DTO, and the shape
/// hook runs after the handler but before the response body is serialized.
/// </summary>
/// <remarks>
/// Apply runs only for <c>IRequest&lt;TResponse&gt;</c> where <c>TResponse</c> is
/// not <see cref="Warp.Core.Handlers.Unit"/>. It does NOT run for
/// <c>IRequest&lt;Unit&gt;</c> (204) or for <c>IStreamRequest&lt;T&gt;</c> (status
/// is fixed for the stream's lifetime).
/// </remarks>
public interface IHttpResponseShape
{
    void Apply(HttpContext context);
}
