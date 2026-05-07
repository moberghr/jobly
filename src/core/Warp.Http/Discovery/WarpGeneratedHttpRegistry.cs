namespace Warp.Http.Discovery;

/// <summary>
/// Drop-off point for the Warp.Http source generator. Each consumer assembly that
/// references <c>Warp.Http</c> gets a <c>[ModuleInitializer]</c>-driven registration
/// pushed here at assembly load. <see cref="ServiceCollectionExtensions.AddWarpHttp"/>
/// reads the snapshot, and <see cref="EndpointRouteBuilderExtensions.MapWarpHttp"/>
/// walks it to register ASP.NET endpoints. Mirrors
/// <c>Warp.Core.Handlers.WarpGeneratedHandlerRegistry</c>.
/// </summary>
public static class WarpGeneratedHttpRegistry
{
    private static readonly Lock _gate = new();
    private static readonly List<HttpEndpointDescriptor> _descriptors = [];

    public static void Add(HttpEndpointDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        lock (_gate)
        {
            _descriptors.Add(descriptor);
        }
    }

    /// <summary>
    /// Snapshot of all descriptors registered so far. Returns a copy — callers may
    /// freely iterate without holding the gate.
    /// </summary>
    public static IReadOnlyList<HttpEndpointDescriptor> Snapshot()
    {
        lock (_gate)
        {
            return [.. _descriptors];
        }
    }
}
