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
        var messages = await _context.Set<Message>().CountAsync();
        var batches = await _context.Set<Batch>().CountAsync();

        var totalSucceeded = await _context.Set<Statistic>()
            .Where(x => x.Key == "stats:succeeded")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();
        var totalFailed = await _context.Set<Statistic>()
            .Where(x => x.Key == "stats:failed")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();
        var totalDeleted = await _context.Set<Statistic>()
            .Where(x => x.Key == "stats:deleted")
            .Select(x => x.Value)
            .FirstOrDefaultAsync();
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

        var hourlyStats = await _context.Set<Statistic>()
            .Where(x => x.Key.StartsWith("stats:succeeded:") || x.Key.StartsWith("stats:failed:"))
            .ToListAsync();

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
        return await Jobs()
            .Where(x => x.CurrentState == state)
            .CountAsync();
    }

    private async Task<int> GetProcessingJobsCount()
    {
        return await Jobs()
            .Where(x => x.CurrentState == State.Processing)
            .CountAsync();
    }
}
