namespace Warp.Http;

/// <summary>
/// Tags a handler class — <c>IRequestHandler&lt;TRequest, TResponse&gt;</c> or
/// <c>IStreamRequestHandler&lt;TRequest, TResponse&gt;</c> — for HTTP exposure.
/// Multiple attributes may be applied to the same handler class to produce versioning
/// aliases; when multiple attributes are present, each must specify a distinct
/// <see cref="Name"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public class WarpHttpAttribute : Attribute
{
    public WarpHttpAttribute(string method, string route)
    {
        Method = method;
        Route = route;
    }

    public string Method { get; }

    public string Route { get; }

    /// <summary>
    /// Optional named group. <see cref="EndpointRouteBuilderExtensions.MapWarpHttp"/> registers
    /// only descriptors whose group strictly matches the argument (null matches null).
    /// </summary>
    public string? Group { get; set; }

    /// <summary>
    /// Optional endpoint name (becomes <c>RouteEndpoint.DisplayName</c> /
    /// OpenAPI operationId). Required when the handler class carries multiple
    /// <see cref="WarpHttpAttribute"/> instances.
    /// </summary>
    public string? Name { get; set; }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class WarpHttpGetAttribute : WarpHttpAttribute
{
    public WarpHttpGetAttribute(string route)
        : base("GET", route)
    {
    }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class WarpHttpPostAttribute : WarpHttpAttribute
{
    public WarpHttpPostAttribute(string route)
        : base("POST", route)
    {
    }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class WarpHttpPutAttribute : WarpHttpAttribute
{
    public WarpHttpPutAttribute(string route)
        : base("PUT", route)
    {
    }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class WarpHttpPatchAttribute : WarpHttpAttribute
{
    public WarpHttpPatchAttribute(string route)
        : base("PATCH", route)
    {
    }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class WarpHttpDeleteAttribute : WarpHttpAttribute
{
    public WarpHttpDeleteAttribute(string route)
        : base("DELETE", route)
    {
    }
}
