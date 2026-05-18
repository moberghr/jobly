using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Warp.Core.BackgroundServices;

/// <summary>
/// <c>ILogger</c> implementation that forwards log calls to a
/// <see cref="IBackgroundServiceLogCollector"/> with <c>Source = User</c>.
/// Returned by <see cref="BackgroundServiceLoggerProvider"/> only for the category that
/// matches the concrete service type's full name; all other categories get a no-op logger.
/// </summary>
internal sealed class BackgroundServiceLogger : ILogger
{
    private readonly LogLevel _minLogLevel;
    private readonly IBackgroundServiceLogCollector _collector;

    internal BackgroundServiceLogger(LogLevel minLogLevel, IBackgroundServiceLogCollector collector)
    {
        _minLogLevel = minLogLevel;
        _collector = collector;
    }

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevel) => logLevel >= _minLogLevel;

    /// <inheritdoc/>
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
        => NullLogger.Instance.BeginScope(state);

    /// <inheritdoc/>
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);
        _collector.Enqueue(BackgroundServiceLogSource.User, logLevel, message, exception);
    }
}
