using System.Collections.Concurrent;
using System.Reflection;

namespace Jobly.Core.Handlers;

public static class MetadataFactory
{
    private static readonly ConcurrentDictionary<Type, Func<Dictionary<string, object>, object>> Factories = new();

    public static T Create<T>(Dictionary<string, object> source)
        where T : class, IJobMetadata
    {
        var factory = Factories.GetOrAdd(typeof(T), static interfaceType =>
        {
            var attr = interfaceType.GetCustomAttribute<MetadataImplementationAttribute>()
                ?? throw new InvalidOperationException(
                    $"No [MetadataImplementation] attribute found on {interfaceType.Name}. " +
                    $"Ensure the Jobly source generator has run for this interface.");

            var implType = attr.ImplementationType;

            // The generated class extends Dictionary<string, object> and has a
            // constructor that copies from an existing dictionary.
            var ctor = implType.GetConstructor([typeof(Dictionary<string, object>)])
                ?? throw new InvalidOperationException(
                    $"Generated type {implType.Name} must have a constructor that accepts Dictionary<string, object>.");

            return dict => ctor.Invoke([dict]);
        });

        return (T)factory(source);
    }
}
