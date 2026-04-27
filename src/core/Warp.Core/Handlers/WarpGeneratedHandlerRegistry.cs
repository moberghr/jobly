using Microsoft.Extensions.DependencyInjection;

namespace Warp.Core.Handlers;

/// <summary>
/// Drop-off point for the Warp source generator. Each consumer assembly with
/// <c>Warp.SourceGenerator</c> referenced emits a <c>[ModuleInitializer]</c> that
/// pushes its handler/behavior DI registrations here at assembly load, and
/// <c>AddWarp</c> replays them onto the user's <see cref="IServiceCollection"/>.
/// </summary>
public static class WarpGeneratedHandlerRegistry
{
    private static readonly Lock _gate = new();
    private static readonly List<Action<IServiceCollection>> _registrations = [];

    public static void Add(Action<IServiceCollection> register)
    {
        ArgumentNullException.ThrowIfNull(register);

        lock (_gate)
        {
            _registrations.Add(register);
        }
    }

    internal static void ApplyAll(IServiceCollection services)
    {
        Action<IServiceCollection>[] snapshot;
        lock (_gate)
        {
            snapshot = [.. _registrations];
        }

        foreach (var register in snapshot)
        {
            register(services);
        }
    }
}
