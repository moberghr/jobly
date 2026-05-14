using System.Reflection;
using System.Text.RegularExpressions;

namespace Warp.Core.Sagas;

/// <summary>
/// Startup-time guard against accidental PII in saga correlation keys. Correlation keys appear
/// in <c>JobLog.Message</c> rows, OpenTelemetry tags, and the distributed-lock name — none of
/// which are PII-safe storage. This check throws <see cref="SagaConfigurationException"/> at
/// <see cref="SagaServiceConfiguration.AddSagaHandler{THandler}"/> if a <c>[Correlate]</c>
/// property's name matches a high-confidence PII regex.
/// </summary>
/// <remarks>
/// False positives are inevitable. Users who have a property genuinely named <c>Email</c> but
/// holding an opaque token (e.g. a hashed identifier) can set
/// <see cref="CorrelateAttribute.IsAnonymized"/> to suppress the check.
/// </remarks>
internal static partial class SagaPiiCheck
{
    [GeneratedRegex(
        @"^(?:email|emailaddress|phone|phonenumber|ssn|socialsecuritynumber|taxid|creditcard|cardnumber|password|firstname|lastname|fullname|dateofbirth|dob)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking)]
    private static partial Regex PiiNameRegex();

    /// <summary>
    /// Exposed for tests so the full pattern set can be exercised without spinning up a fresh
    /// type per pattern. Production callers go through <see cref="ValidateMessageType"/>.
    /// </summary>
    internal static bool IsPiiName(string propertyName) => PiiNameRegex().IsMatch(propertyName);

    public static void ValidateMessageType(Type messageType)
    {
        var correlated = messageType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<CorrelateAttribute>() != null)
            .ToArray();

        foreach (var property in correlated)
        {
            var attr = property.GetCustomAttribute<CorrelateAttribute>()!;
            if (attr.IsAnonymized)
            {
                continue;
            }

            if (PiiNameRegex().IsMatch(property.Name))
            {
                throw new SagaConfigurationException(
                    $"Message {messageType.FullName} declares a [Correlate] property named " +
                    $"'{property.Name}', which matches a PII-suggestive pattern. Correlation keys " +
                    $"appear in JobLog.Message rows, OpenTelemetry tags, and distributed-lock " +
                    $"names — none of which are PII-safe. Either rename the property to an opaque " +
                    $"identifier (e.g. OrderId, AccountId), or set [Correlate(IsAnonymized = true)] " +
                    $"if the value is actually a hash/token rather than the raw PII.");
            }
        }
    }
}
