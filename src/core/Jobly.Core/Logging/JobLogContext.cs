using Jobly.Core.Data.Entities;

namespace Jobly.Core.Logging;

public class JobLogCollector
{
    public Guid JobId { get; set; }

    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    public Guid? WorkerId { get; set; }

    public List<JobLog> Entries { get; } = [];

    public void Add(string level, string message, string? exception = null)
    {
        Entries.Add(new JobLog
        {
            JobId = JobId,
            Timestamp = TimeProvider.GetUtcNow().UtcDateTime,
            Level = level,
            Message = message,
            Exception = exception,
            WorkerId = WorkerId,
        });
    }
}

public static class JobLogContext
{
    private static readonly AsyncLocal<JobLogCollector?> _current = new();

    public static JobLogCollector? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }
}
