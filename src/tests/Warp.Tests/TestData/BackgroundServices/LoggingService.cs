using Microsoft.Extensions.Logging;
using Warp.Core.BackgroundServices;

namespace Warp.Tests.TestData.BackgroundServices;

/// <summary>
/// <see cref="WarpBackgroundService"/> that emits a set of log messages at various levels and
/// then blocks on a barrier. Tests can pin the service inside <c>ExecuteAsync</c> to ensure the
/// log entries are captured before asserting on the database.
/// Emits: <c>LogInformation</c>, <c>LogWarning</c>, and <c>LogDebug</c> — the debug entry
/// exercises the default level-filter (Information threshold drops it unless overridden).
/// </summary>
public sealed class LoggingService : WarpBackgroundService
{
    private readonly ILogger<LoggingService> _logger;
    private readonly LoggingServiceSignal _signal;

    public LoggingService(ILogger<LoggingService> logger, LoggingServiceSignal signal)
    {
        _logger = logger;
        _signal = signal;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("hello {n}", 42);
        _logger.LogWarning("warn");
        _logger.LogDebug("debug");

        _signal.Logged.Release();
        await _signal.CanFinish.WaitAsync(ct);
    }
}

/// <summary>
/// Coordination signals for <see cref="LoggingService"/>. Register as singleton in the test DI
/// so the test and service share the same instance.
/// </summary>
public sealed class LoggingServiceSignal
{
    /// <summary>Released by the service after emitting log messages.</summary>
    public SemaphoreSlim Logged { get; } = new(0);

    /// <summary>Release to let <c>ExecuteAsync</c> return on cancellation.</summary>
    public SemaphoreSlim CanFinish { get; } = new(0);
}

/// <summary>
/// Variant of <see cref="LoggingService"/> with <see cref="MinLogLevel"/> overridden to
/// <see cref="LogLevel.Debug"/>, used by tests that assert Debug-level entries are captured when
/// the threshold is explicitly lowered.
/// </summary>
public sealed class DebugLoggingService : WarpBackgroundService
{
    private readonly ILogger<DebugLoggingService> _logger;
    private readonly LoggingServiceSignal _signal;

    public override LogLevel MinLogLevel => LogLevel.Debug;

    public DebugLoggingService(ILogger<DebugLoggingService> logger, LoggingServiceSignal signal)
    {
        _logger = logger;
        _signal = signal;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogDebug("debug-captured");
        _logger.LogInformation("info-captured");

        _signal.Logged.Release();
        await _signal.CanFinish.WaitAsync(ct);
    }
}
