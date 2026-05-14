using System.Text.Json.Serialization;

namespace Warp.Core.Sagas;

/// <summary>
/// Base class for stateful sagas. Subclasses hold the correlated state — fields and properties
/// declared on the subclass are JSON-serialized into <c>StateJson</c> on the underlying
/// <see cref="Warp.Core.Data.Entities.SagaState"/> row.
/// </summary>
/// <remarks>
/// Completion is signalled via <see cref="MarkCompleted"/>. The pipeline proxy reads
/// <see cref="IsCompleted"/> after the handler returns and deletes the row in the same
/// <c>SaveChanges</c> when set. Mirrors Wolverine's complete-equals-delete convention so the
/// correlation key can be reused immediately.
/// </remarks>
public abstract class Saga
{
    private bool _isCompleted;

    // All base-class properties are [JsonIgnore]'d: the authoritative storage for Id and
    // CorrelationKey is the SagaState row's columns (the store reassigns them on Load), so
    // duplicating them in StateJson is pure bloat. IsCompleted is a transient runtime flag —
    // a true value deletes the row, so persisting it would only ever record "false".
    [JsonIgnore]
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonIgnore]
    public string CorrelationKey { get; set; } = string.Empty;

    /// <summary>
    /// Marks this saga as complete. The pipeline proxy removes the persisted row on the next save.
    /// Public so handler code can call it directly — restricting to <c>protected</c> forced reflection
    /// from outside the saga subclass.
    /// </summary>
    public void MarkCompleted() => _isCompleted = true;

    /// <summary>
    /// True when the handler has called <see cref="MarkCompleted"/>. The proxy reads this after
    /// the handler returns; outside the framework, users should call <see cref="MarkCompleted"/>
    /// rather than reading this flag.
    /// </summary>
    [JsonIgnore]
    public bool IsCompleted => _isCompleted;
}

/// <summary>
/// Typed projection of <see cref="Saga.CorrelationKey"/>. The persisted column is still a
/// single <c>string</c>; <see cref="Key"/> canonicalizes to/from the typed value via
/// <see cref="SagaCorrelationKeyConverter"/> so user code can work with <see cref="Guid"/>,
/// <see cref="int"/>, or <see cref="long"/> directly instead of stringly-typed correlation IDs.
/// </summary>
/// <typeparam name="TKey">
/// One of: <see cref="string"/>, <see cref="Guid"/>, <see cref="int"/>, <see cref="long"/>.
/// Other types throw <see cref="SagaConfigurationException"/> on the first read or write of
/// <see cref="Key"/>.
/// </typeparam>
public abstract class Saga<TKey> : Saga
    where TKey : notnull
{
    /// <summary>
    /// Typed view of <see cref="Saga.CorrelationKey"/>. Reads parse the persisted canonical
    /// string; writes canonicalize the typed value and store it. For <see cref="Guid"/> the
    /// canonical form is the <c>"N"</c> format (32 hex chars, no dashes) — the same format the
    /// framework uses when reading <c>[Correlate]</c> properties off inbound messages.
    /// </summary>
    /// <remarks>
    /// <c>[JsonIgnore]</c> is required: without it, default <see cref="System.Text.Json.JsonSerializer"/>
    /// would write <em>both</em> <c>CorrelationKey</c> and <c>Key</c> to <c>StateJson</c>, bloating
    /// the row and creating a property-order-dependent round-trip. The persisted truth is
    /// <c>CorrelationKey</c>; <c>Key</c> is purely a typed projection.
    /// </remarks>
    [JsonIgnore]
    public TKey Key
    {
        get => SagaCorrelationKeyConverter.FromCanonical<TKey>(CorrelationKey);
        set => CorrelationKey = SagaCorrelationKeyConverter.ToCanonical(value);
    }
}
