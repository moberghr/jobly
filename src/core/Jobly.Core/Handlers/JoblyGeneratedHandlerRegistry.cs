using Microsoft.Extensions.DependencyInjection;

namespace Jobly.Core.Handlers;

/// <summary>
/// Drop-off point for the Jobly source generator. Each consumer assembly with
/// <c>Jobly.SourceGenerator</c> referenced emits a <c>[ModuleInitializer]</c> that
/// pushes its handler/behavior DI registrations here at assembly load, and
/// <c>AddJobly</c> replays them onto the user's <see cref="IServiceCollection"/>.
/// </summary>
public static class JoblyGeneratedHandlerRegistry
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
