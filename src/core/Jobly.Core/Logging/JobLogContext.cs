using System.Collections.Concurrent;
using Jobly.Core.Data.Entities;

namespace Jobly.Core.Logging;

public class JobLogCollector
{
    public Guid JobId { get; set; }

    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    public Guid? WorkerId { get; set; }

    private readonly ConcurrentQueue<JobLog> _entries = new();

    public void Add(string level, string message, string? exception = null)
    {
        _entries.Enqueue(new JobLog
        {
            JobId = JobId,
            Timestamp = TimeProvider.GetUtcNow().UtcDateTime,
            Level = level,
            Message = message,
            Exception = exception,
            WorkerId = WorkerId,
        });
    }

    public List<JobLog> Drain()
    {
        var list = new List<JobLog>();
        while (_entries.TryDequeue(out var entry))
        {
            list.Add(entry);
        }

        return list;
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
