namespace Warp.Core.Sagas;

/// <summary>
/// Marks the property on an <c>IMessage</c> type that holds the saga correlation key.
/// The saga pipeline reads this property at dispatch time to locate (or create) the
/// matching saga instance. The property type must be <see cref="string"/>, <see cref="System.Guid"/>,
/// <see cref="int"/>, or <see cref="long"/>; the framework canonicalizes the value to a single
/// string format for storage.
/// </summary>
/// <example>
/// <code>
/// public class OrderPlaced : IMessage
/// {
///     [Correlate]
///     public string OrderId { get; set; } = string.Empty;
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class CorrelateAttribute : Attribute
{
    /// <summary>
    /// Set to <c>true</c> if the property name happens to match a PII-suggestive regex
    /// (e.g. a property genuinely called <c>Email</c> that holds an opaque hashed token, not a
    /// real email). Suppresses the startup-time PII check in
    /// <see cref="SagaServiceConfiguration.AddSagaHandler{THandler}"/>.
    /// </summary>
    public bool IsAnonymized { get; init; }
}
