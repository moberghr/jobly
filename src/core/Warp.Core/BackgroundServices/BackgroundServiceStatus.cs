namespace Warp.Core.BackgroundServices;

/// <summary>
/// Operational status of a <c>WarpBackgroundService</c> instance on a specific server.
/// </summary>
public enum BackgroundServiceStatus
{
    /// <summary>
    /// The service is executing <c>ExecuteAsync</c>.
    /// </summary>
    Running = 1,

    /// <summary>
    /// The service is waiting to acquire the singleton lease. Only applicable when
    /// <c>ServiceScope == Singleton</c>.
    /// </summary>
    Waiting = 2,

    /// <summary>
    /// <c>ExecuteAsync</c> threw an unhandled exception or returned without cancellation.
    /// The supervisor will restart after exponential backoff.
    /// </summary>
    Faulted = 3,

    /// <summary>
    /// The supervisor is in its backoff wait before the next <c>ExecuteAsync</c> invocation.
    /// </summary>
    Restarting = 4,

    /// <summary>
    /// The service's declared <c>ServiceScope</c> does not match the value stored in the
    /// <c>BackgroundServiceDefinition</c> row. The supervisor refuses to start until the
    /// mismatch is resolved (complete or roll back the deploy).
    /// </summary>
    ConfigurationMismatch = 5,
}
