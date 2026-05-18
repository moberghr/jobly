using Microsoft.Extensions.Logging;

namespace Warp.Core.BackgroundServices;

/// <summary>
/// Abstract base class for long-lived in-process services that Warp manages and surfaces in the
/// dashboard. Designed for one-line migration from .NET's <c>BackgroundService</c>: rename the
/// base class, remove the <c>StartAsync</c>/<c>StopAsync</c> overrides, and register via
/// <c>opt.AddBackgroundService&lt;T&gt;()</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Lifetime: Singleton.</strong> Subclasses are registered as singletons in DI.
/// Do not inject scoped services directly into the constructor — EF Core <c>DbContext</c>,
/// per-request services, and similar scoped registrations will cause a captive-scoped-dependency
/// error at startup when <c>ValidateScopes = true</c> (always set in Development). Instead,
/// inject <c>IServiceScopeFactory</c> and create a scope per unit of work, exactly as
/// recommended for plain <c>BackgroundService</c>.
/// </para>
/// <para>
/// <strong>PII responsibility (§1.2).</strong> When handler logging is active, <c>ILogger</c>
/// calls from inside <see cref="ExecuteAsync"/> are captured and written to the
/// <c>background_service_log</c> table, making them visible in the Warp dashboard. Treat any
/// user payload, PII, or sensitive identifier the same way you would treat <c>JobLog.Message</c>
/// — do not log raw user data at <c>Information</c> level or above. Use
/// <see cref="MinLogLevel"/> to raise the capture threshold if necessary.
/// </para>
/// </remarks>
public abstract class WarpBackgroundService
{
    /// <summary>
    /// Display name used for dashboard surfaces, log rows, and lease coordination.
    /// Defaults to the concrete type name; override to provide a friendlier identifier.
    /// </summary>
    public virtual string Name => GetType().Name;

    /// <summary>
    /// Controls how many instances run across the cluster.
    /// <see cref="ServiceScope.PerServer"/> (default) runs one independent copy per server.
    /// <see cref="ServiceScope.Singleton"/> elects exactly one holder via a lease; other
    /// servers wait and take over on graceful shutdown or lease expiry (~30s on hard kill).
    /// </summary>
    public virtual ServiceScope Scope => ServiceScope.PerServer;

    /// <summary>
    /// Minimum log level captured from this service's <c>ILogger</c> calls and written to the
    /// dashboard log table. Entries below this threshold are dropped at the collector.
    /// Defaults to <see cref="LogLevel.Information"/>.
    /// </summary>
    public virtual LogLevel MinLogLevel => LogLevel.Information;

    /// <summary>
    /// Per-service override for the maximum number of captured log rows to retain.
    /// <c>null</c> (default) falls back to <c>WarpConfiguration.BackgroundServiceLogRetentionCount</c>.
    /// </summary>
    public virtual int? LogRetentionCountOverride => null;

    /// <summary>
    /// Per-service override for the maximum age of captured log rows.
    /// <c>null</c> (default) falls back to <c>WarpConfiguration.BackgroundServiceLogRetentionAge</c>.
    /// </summary>
    public virtual TimeSpan? LogRetentionAgeOverride => null;

    /// <summary>
    /// Entry point for the service's long-lived work loop. Invoked by the Warp supervisor
    /// after the instance row is created and (for <see cref="ServiceScope.Singleton"/> services)
    /// after the cluster lease is acquired. The <paramref name="ct"/> is cancelled on graceful
    /// host shutdown or on singleton lease loss — observe it to exit cleanly.
    /// </summary>
    protected abstract Task ExecuteAsync(CancellationToken ct);

    /// <summary>
    /// Called by the Warp host to invoke <see cref="ExecuteAsync"/>. Internal so that the
    /// supervisor can dispatch through the base-class reference without reflection, while
    /// keeping <see cref="ExecuteAsync"/> <c>protected</c> for subclass authors.
    /// </summary>
    internal Task InvokeExecuteAsync(CancellationToken ct) => ExecuteAsync(ct);
}
