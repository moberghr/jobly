namespace Warp.Core.Sagas;

/// <summary>
/// Why a <see cref="SagaSaveConflictException"/> was raised. Drives the proxy's requeue
/// telemetry tag and log message.
/// </summary>
public enum SagaSaveConflictKind
{
    /// <summary>
    /// Optimistic concurrency violation on the saga's <c>Version</c> column — another worker
    /// committed a competing update between this proxy's <c>Load</c> and <c>SaveChanges</c>.
    /// </summary>
    Version = 1,

    /// <summary>
    /// Unique-index violation on <c>(Type, CorrelationKey)</c> — two <c>[StartsSaga]</c> messages
    /// for the same correlation key raced past the mutex (transient lock-provider hiccup) and
    /// both attempted to insert.
    /// </summary>
    UniqueConstraint = 2,
}

/// <summary>
/// Raised by <see cref="SagaStore{TContext}.SaveChangesAsync"/> when the commit lost a race
/// with another worker. The proxy translates this into a requeue outcome — the message will
/// be tried again, this time loading the freshly-committed state.
/// </summary>
public sealed class SagaSaveConflictException : Exception
{
    public SagaSaveConflictException()
    {
    }

    public SagaSaveConflictException(string message)
        : base(message)
    {
    }

    public SagaSaveConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public SagaSaveConflictException(SagaSaveConflictKind kind, Exception innerException)
        : base($"Saga save lost a race ({kind}). The message will be requeued.", innerException)
    {
        Kind = kind;
    }

    public SagaSaveConflictKind Kind { get; }
}
