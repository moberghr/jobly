namespace Warp.Core.BackgroundServices;

/// <summary>
/// Indicates who produced a <c>BackgroundServiceLog</c> entry.
/// </summary>
public enum BackgroundServiceLogSource
{
    /// <summary>
    /// Emitted by the Warp supervisor for structured lifecycle events: Started, Faulted,
    /// Restarting, LeaseAcquired, LeaseLost, Stopped, ConfigurationMismatch.
    /// </summary>
    Lifecycle = 1,

    /// <summary>
    /// Captured from the service's own <c>ILogger&lt;T&gt;</c> calls.
    /// </summary>
    User = 2,
}
