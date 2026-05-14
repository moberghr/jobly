using System.Collections.Concurrent;
using System.Reflection;

namespace Warp.Core.Sagas;

/// <summary>
/// Resolves the <see cref="CorrelateAttribute"/> property for a message type, caching the
/// <see cref="PropertyInfo"/> after the first reflection scan. Singleton DI lifetime — one
/// instance per process, shared across all saga proxy invocations.
/// </summary>
/// <remarks>
/// Mirrors <c>JobDispatcher</c>'s per-type cache pattern. Per-message cost after the first
/// lookup is one dictionary read.
/// </remarks>
public sealed class SagaCorrelationCache
{
    private readonly ConcurrentDictionary<Type, PropertyInfo> _properties = new();

    /// <summary>
    /// Returns the canonical string correlation key for a message instance. The
    /// <c>[Correlate]</c> property may be <c>string</c>, <see cref="Guid"/>, <see cref="int"/>,
    /// or <see cref="long"/> — non-string values are canonicalized via
    /// <see cref="SagaCorrelationKeyConverter.ToCanonical"/>. Throws
    /// <see cref="SagaConfigurationException"/> on zero/multiple <c>[Correlate]</c> properties,
    /// unsupported property type, or null/empty value.
    /// </summary>
    public string GetCorrelationKey(object message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var property = _properties.GetOrAdd(message.GetType(), Resolve);
        var raw = property.GetValue(message);
        var canonical = raw == null ? null : SagaCorrelationKeyConverter.ToCanonical(raw);

        if (string.IsNullOrEmpty(canonical))
        {
            throw new SagaConfigurationException(
                $"Message {message.GetType().FullName} property '{property.Name}' " +
                $"is the [Correlate] property but its value is null or empty.");
        }

        return canonical;
    }

    private static PropertyInfo Resolve(Type messageType)
    {
        var correlated = messageType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<CorrelateAttribute>() != null)
            .ToArray();

        if (correlated.Length == 0)
        {
            // Diagnostic-only second pass with NonPublic so a user who marked a
            // protected/internal/private property gets a clear "must be public" error
            // instead of the misleading "has no [Correlate] property at all".
            var nonPublicCandidates = messageType.GetProperties(BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(p => p.GetCustomAttribute<CorrelateAttribute>() != null)
                .ToArray();
            if (nonPublicCandidates.Length > 0)
            {
                var names = string.Join(", ", nonPublicCandidates.Select(p => p.Name));
                throw new SagaConfigurationException(
                    $"Message {messageType.FullName} has [Correlate] on non-public property '{names}'. " +
                    $"The reflector scans only public instance properties — make the property " +
                    $"public (the saga pipeline needs to read its value at dispatch time).");
            }

            throw new SagaConfigurationException(
                $"Message {messageType.FullName} has no [Correlate] property. " +
                $"Sagas require exactly one public string/Guid/int/long property marked with [Correlate].");
        }

        if (correlated.Length > 1)
        {
            var names = string.Join(", ", correlated.Select(p => p.Name));
            throw new SagaConfigurationException(
                $"Message {messageType.FullName} has multiple [Correlate] properties ({names}). " +
                $"v1 supports exactly one correlation key per message.");
        }

        var property = correlated[0];
        if (!SagaCorrelationKeyConverter.IsSupported(property.PropertyType))
        {
            throw new SagaConfigurationException(
                $"Message {messageType.FullName} property '{property.Name}' is marked with " +
                $"[Correlate] but its type is {property.PropertyType.Name}. " +
                $"Supported correlation key types: string, Guid, int, long.");
        }

        return property;
    }
}
