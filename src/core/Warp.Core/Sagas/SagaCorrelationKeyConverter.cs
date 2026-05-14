using System.Globalization;

namespace Warp.Core.Sagas;

/// <summary>
/// Canonicalizes typed correlation keys to/from the single <c>string</c> column the
/// <see cref="Warp.Core.Data.Entities.SagaState"/> table stores.
/// </summary>
/// <remarks>
/// Two callers share this converter:
/// <list type="bullet">
/// <item><see cref="SagaCorrelationCache.GetCorrelationKey"/> when reading a typed
/// <c>[Correlate]</c> property off an inbound <c>IMessage</c>.</item>
/// <item><c>Saga&lt;TKey&gt;.Key</c> when projecting the persisted string back to a typed value.</item>
/// </list>
/// Supported types are <c>string</c>, <see cref="Guid"/>, <see cref="int"/>, <see cref="long"/>.
/// <see cref="Guid"/> uses the <c>"N"</c> format (32 hex chars, no dashes) so callers can't
/// disagree on `D`/`N`/`B`/`P`. Numeric types use invariant culture so European-locale machines
/// don't write keys with comma separators.
/// </remarks>
internal static class SagaCorrelationKeyConverter
{
    // Matches the SagaState.CorrelationKey column length. Keys longer than this would explode at
    // SaveChanges with a database column-truncation error; reject early with a clear message.
    public const int MaxKeyLength = 200;

    public static string ToCanonical(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        // Reject default-valued typed keys before canonicalization. Guid.Empty would canonicalize
        // to "0000...0000" and default(int)/default(long) to "0" — all non-empty so the
        // SagaCorrelationCache's IsNullOrEmpty check passes, silently joining unrelated sagas
        // whose [Correlate] property was forgotten or model-bound from absent input. In a multi-
        // tenant deployment this is a cross-tenant correlation bleed; the mutex named
        // "warp:saga:Type:0000...0000" also becomes a global pinch point.
        switch (value)
        {
            case Guid g when g == Guid.Empty:
                throw new SagaConfigurationException(
                    "Saga correlation key is Guid.Empty — a default-valued [Correlate] property " +
                    "indicates the message's correlation field was never assigned. Initialize it " +
                    "before publishing, or use a non-Empty sentinel if you genuinely need one.");
            case int i when i == 0:
                throw new SagaConfigurationException(
                    "Saga correlation key is 0 — a default-valued [Correlate] property indicates " +
                    "the message's correlation field was never assigned. Initialize it before " +
                    "publishing, or use a non-zero sentinel if you genuinely need one.");
            case long l when l == 0L:
                throw new SagaConfigurationException(
                    "Saga correlation key is 0L — a default-valued [Correlate] property indicates " +
                    "the message's correlation field was never assigned. Initialize it before " +
                    "publishing, or use a non-zero sentinel if you genuinely need one.");
        }

        var canonical = value switch
        {
            string s => NormalizeString(s),
            Guid g => g.ToString("N", CultureInfo.InvariantCulture),
            int i => i.ToString(CultureInfo.InvariantCulture),
            long l => l.ToString(CultureInfo.InvariantCulture),
            _ => throw new SagaConfigurationException(
                $"Type '{value.GetType().FullName}' is not supported as a saga correlation key. " +
                $"Supported: string, Guid, int, long."),
        };

        if (canonical.Length > MaxKeyLength)
        {
            throw new SagaConfigurationException(
                $"Saga correlation key length ({canonical.Length}) exceeds the maximum of " +
                $"{MaxKeyLength}. The SagaState.CorrelationKey column is varchar({MaxKeyLength}); " +
                $"longer keys would also produce ambiguous distributed-lock names. Use a shorter " +
                $"identifier or hash the input.");
        }

        return canonical;
    }

    private static string NormalizeString(string raw)
    {
        // Trim leading/trailing whitespace so that CSV imports, copy-pasted IDs, and form fields
        // with trailing newlines don't produce ghost sagas distinct from their cleaned twins.
        var trimmed = raw.Trim();

        if (trimmed.Length == 0)
        {
            throw new SagaConfigurationException(
                "Saga correlation key is empty (or whitespace-only) after trimming. The message's " +
                "[Correlate] property must hold a meaningful identifier.");
        }

        return trimmed;
    }

    public static TKey FromCanonical<TKey>(string canonical)
    {
        ArgumentNullException.ThrowIfNull(canonical);

        if (typeof(TKey) == typeof(string))
        {
            return (TKey)(object)canonical;
        }

        try
        {
            if (typeof(TKey) == typeof(Guid))
            {
                return (TKey)(object)Guid.ParseExact(canonical, "N");
            }

            if (typeof(TKey) == typeof(int))
            {
                return (TKey)(object)int.Parse(canonical, CultureInfo.InvariantCulture);
            }

            if (typeof(TKey) == typeof(long))
            {
                return (TKey)(object)long.Parse(canonical, CultureInfo.InvariantCulture);
            }
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            // Wrap the raw parse error so operators see a saga-context message — e.g. a legacy
            // string correlation key carrying email/UUID-D format won't parse as Guid "N", and
            // the bare FormatException ("Input string was not in a correct format") is unhelpful.
            throw new SagaConfigurationException(
                $"Saga correlation key '{canonical}' is not a valid {typeof(TKey).Name}. " +
                $"Expected canonical form: " + CanonicalFormDescription<TKey>() + ".",
                ex);
        }

        throw new SagaConfigurationException(
            $"Type '{typeof(TKey).FullName}' is not supported as a saga correlation key. " +
            $"Supported: string, Guid, int, long.");
    }

    private static string CanonicalFormDescription<TKey>()
    {
        if (typeof(TKey) == typeof(Guid))
        {
            return "32 hex chars, no dashes (Guid \"N\" format)";
        }

        if (typeof(TKey) == typeof(int) || typeof(TKey) == typeof(long))
        {
            return "invariant-culture decimal digits";
        }

        return "string";
    }

    public static bool IsSupported(Type type)
    {
        return type == typeof(string)
            || type == typeof(Guid)
            || type == typeof(int)
            || type == typeof(long);
    }
}
