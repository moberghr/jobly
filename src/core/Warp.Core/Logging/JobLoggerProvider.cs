using Microsoft.Extensions.Logging;

namespace Warp.Core.Logging;

public class JobLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new JobLogger(categoryName);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}

public class JobLogger : ILogger
{
    private readonly string _categoryName;

    public JobLogger(string categoryName)
    {
        _categoryName = categoryName;
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
        => null;

    public bool IsEnabled(LogLevel logLevel) => JobLogContext.Current != null && logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var collector = JobLogContext.Current;
        if (collector == null || collector.JobId == Guid.Empty)
        {
            return;
        }

        if (logLevel < LogLevel.Information)
        {
            return;
        }

        var message = formatter(state, exception);
        var level = logLevel switch
        {
            LogLevel.Information => "Information",
            LogLevel.Warning => "Warning",
            LogLevel.Error => "Error",
            LogLevel.Critical => "Critical",
            _ => logLevel.ToString(),
        };

        collector.Add(level, $"[{_categoryName}] {message}", exception?.ToString());
    }
}
