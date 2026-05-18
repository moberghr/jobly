using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Warp.Core.BackgroundServices;

/// <summary>
/// <c>ILoggerProvider</c> that routes log calls from a specific service category into the
/// per-instance <see cref="IBackgroundServiceLogCollector"/>. Created once per running service
/// instance by the supervisor — not registered in DI.
/// </summary>
/// <remarks>
/// <c>CreateLogger(category)</c> returns a capturing <see cref="BackgroundServiceLogger"/>
/// only when the category exactly matches the service's type full name.
/// All other categories receive <c>NullLogger.Instance</c> so unrelated framework logs are
/// not accidentally captured into the service's log table.
/// </remarks>
public sealed class BackgroundServiceLoggerProvider : ILoggerProvider
{
    private readonly string _serviceCategoryName;
    private readonly LogLevel _minLogLevel;
    private readonly IBackgroundServiceLogCollector _collector;

    public BackgroundServiceLoggerProvider(
        string serviceCategoryName,
        LogLevel minLogLevel,
        IBackgroundServiceLogCollector collector)
    {
        _serviceCategoryName = serviceCategoryName;
        _minLogLevel = minLogLevel;
        _collector = collector;
    }

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName)
    {
        if (!string.Equals(categoryName, _serviceCategoryName, StringComparison.Ordinal))
        {
            return NullLogger.Instance;
        }

        return new BackgroundServiceLogger(_minLogLevel, _collector);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
