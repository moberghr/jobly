using System.Globalization;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Jobly.Core.Services;

public interface IDashboardStatsService
{
    Task<DashboardStatistics> GetJoblyStatus();

    Task<List<StatsHistoryPoint>> GetStatsHistory(int hours = 24);

    Task<List<ServerModel>> GetServers();

    Task<int> GetServerCount();
}

public class DashboardStatsService<TContext> : IDashboardStatsService
    where TContext : DbContext
{
    private readonly TContext _context;

    public DashboardStatsService(TContext context)
    {
        _context = context;
    }

    public async Task<DashboardStatistics> GetJoblyStatus()
    {
        var total = await GetTotalJobsCount();
        var pending = await GetPendingJobsCount();
        var scheduled = await GetScheduledJobsCount();
        var created = await GetJobsCount(State.Enqueued);
        var completed = await GetJobsCount(State.Completed);
        var failed = await GetJobsCount(State.Failed);
        var processing = await GetProcessingJobsCount();

        var servers = await GetServerCount();
        var awaiting = await GetJobsCount(State.Awaiting);
        var messages = await _context.Set<Message>()
            .Where(x => x.CurrentState != State.Completed)
            .CountAsync();
        var batches = await _context.Set<Batch>()
            .Where(x => x.Counter > 0)
            .CountAsync();

        var totalSucceeded = await GetCombinedStatValue("stats:succeeded");
        var totalFailed = await GetCombinedStatValue("stats:failed");
        var totalDeleted = await GetCombinedStatValue("stats:deleted");
        var model = new DashboardStatistics
        {
            Total = total,
            Pending = pending,
            Scheduled = scheduled,
            Created = created,
            Completed = completed,
            Failed = failed,
            Processing = processing,
            Servers = servers,
            Awaiting = awaiting,
            Messages = messages,
            Batches = batches,
            TotalSucceeded = totalSucceeded,
            TotalFailed = totalFailed,
            TotalDeleted = totalDeleted,
            TotalCreated = 0,
            DatabaseConnection = GetSafeDatabaseConnection(),
        };

        return model;
    }

    public async Task<int> GetServerCount()
    {
        return await _context.Set<Server>().CountAsync();
    }

    public async Task<List<ServerModel>> GetServers()
    {
        var servers = await _context.Set<Server>().ToListAsync();

        var workers = await _context.Set<Worker>().ToListAsync();

        var processingJobs = await _context.Set<Job>()
            .Where(x => x.CurrentState == State.Processing)
            .Where(x => x.CurrentWorkerId != null)
            .Select(x => new { x.CurrentWorkerId, x.Id, x.Type })
            .ToListAsync();

        var jobByWorker = processingJobs.ToDictionary(j => j.CurrentWorkerId!.Value);

        var workersByServer = workers
            .GroupBy(w => w.ServerId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return servers.ConvertAll(s => new ServerModel
        {
            Id = s.Id,
            ServerName = s.ServerName,
            StartedTime = s.StartedTime,
            LastHeartbeatTime = s.LastHeartbeatTime,
            ServiceCount = s.ServiceCount,
            CpuUsagePercent = s.CpuUsagePercent,
            MemoryWorkingSetBytes = s.MemoryWorkingSetBytes,
            Workers = workersByServer.GetValueOrDefault(s.Id, [])
                .ConvertAll(w =>
                {
                    jobByWorker.TryGetValue(w.Id, out var activeJob);
                    return new WorkerModel
                    {
                        WorkerId = w.Id,
                        StartedTime = w.StartedTime,
                        LastHeartbeatTime = w.LastHeartbeatTime,
                        CurrentJobId = activeJob?.Id,
                        CurrentJobType = activeJob?.Type,
                    };
                }),
        });
    }

    public async Task<List<StatsHistoryPoint>> GetStatsHistory(int hours = 24)
    {
        var since = DateTime.UtcNow.AddHours(-hours);

        var aggregated = await _context.Set<Statistic>()
            .Where(x => x.Key.StartsWith("stats:succeeded:") || x.Key.StartsWith("stats:failed:"))
            .Select(x => new { x.Key, x.Value })
            .ToListAsync();

        var pending = await _context.Set<Counter>()
            .Where(x => x.Key.StartsWith("stats:succeeded:") || x.Key.StartsWith("stats:failed:"))
            .GroupBy(x => x.Key)
            .Select(g => new { Key = g.Key, Value = (long)g.Sum(c => c.Value) })
            .ToListAsync();

        // Merge both into a single list
        var hourlyStats = aggregated.Concat(pending)
            .GroupBy(x => x.Key)
            .Select(g => new { Key = g.Key, Value = g.Sum(x => x.Value) })
            .ToList();

        // Parse keys like "stats:succeeded:2026-03-28-14" into date + metric
        var points = new Dictionary<string, StatsHistoryPoint>(StringComparer.Ordinal);

        foreach (var stat in hourlyStats)
        {
            var parts = stat.Key.Split(':');
            if (parts.Length != 3)
            {
                continue;
            }

            var metric = parts[1]; // "succeeded" or "failed"
            var hourStr = parts[2]; // "2026-03-28-14"

            if (!DateTime.TryParseExact(
                hourStr,
                "yyyy-MM-dd-HH",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var hour))
            {
                continue;
            }

            if (hour < since)
            {
                continue;
            }

            if (!points.TryGetValue(hourStr, out var point))
            {
                point = new StatsHistoryPoint { Hour = hour };
                points[hourStr] = point;
            }

            if (string.Equals(metric, "succeeded", StringComparison.Ordinal))
            {
                point.Succeeded = stat.Value;
            }
            else if (string.Equals(metric, "failed", StringComparison.Ordinal))
            {
                point.Failed = stat.Value;
            }
        }

        return [.. points.Values.OrderBy(p => p.Hour)];
    }

    private async Task<long> GetCombinedStatValue(string key)
    {
        var aggregated = await _context.Set<Statistic>()
            .Where(x => x.Key == key)
            .Select(x => x.Value)
            .FirstOrDefaultAsync();

        var pending = await _context.Set<Counter>()
            .Where(x => x.Key == key)
            .SumAsync(x => x.Value);

        return aggregated + pending;
    }

    /// <summary>
    /// Base query that excludes batch placeholder jobs from results.
    /// </summary>
    private IQueryable<Job> Jobs()
    {
        var batchIds = _context.Set<Batch>().Select(b => b.Id);
        return _context.Set<Job>().Where(j => !batchIds.Contains(j.Id));
    }

    private async Task<int> GetTotalJobsCount()
    {
        return await Jobs().CountAsync();
    }

    private async Task<int> GetPendingJobsCount()
    {
        return await Jobs()
            .Where(x => x.ScheduleTime < DateTime.UtcNow)
            .CountAsync();
    }

    private async Task<int> GetScheduledJobsCount()
    {
        return await Jobs()
            .Where(x => x.ScheduleTime > DateTime.UtcNow)
            .CountAsync();
    }

    private async Task<int> GetJobsCount(State state)
    {
        var query = Jobs()
            .Where(x => x.CurrentState == state);

        if (state == State.Enqueued)
        {
            query = query.Where(x => x.ScheduleTime <= DateTime.UtcNow);
        }

        return await query.CountAsync();
    }

    private string? GetSafeDatabaseConnection()
    {
        var connectionString = _context.Database.GetConnectionString();
        if (string.IsNullOrEmpty(connectionString))
        {
            return null;
        }

        var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Contains('='))
            .ToDictionary(
                p => p[..p.IndexOf('=')].Trim(),
                p => p[(p.IndexOf('=') + 1)..].Trim(),
                StringComparer.OrdinalIgnoreCase);

        var isPostgres = parts.ContainsKey("Host");
        var provider = isPostgres ? "PostgreSQL Server" : "SQL Server";
        var host = parts.GetValueOrDefault("Host") ?? parts.GetValueOrDefault("Server") ?? parts.GetValueOrDefault("Data Source") ?? "unknown";
        var db = parts.GetValueOrDefault("Database") ?? parts.GetValueOrDefault("Initial Catalog") ?? "";

        return $"{provider}: Host: {host}, DB: {db}";
    }

    private async Task<int> GetProcessingJobsCount()
    {
        return await Jobs()
            .Where(x => x.CurrentState == State.Processing)
            .CountAsync();
    }
}
