using Handfire.Core.Entities;
using Handfire.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Handfire.Core;

public class HandfireService<TContext> : IHandfireService
    where TContext : DbContext
{
    private readonly TContext _context;

    public HandfireService(TContext context)
    {
        _context = context;
    }

    public async Task<int> GetTotalJobs()
    {

        var counter = await _context.Set<Job>()
            .CountAsync();

        return counter;
    }

    public async Task<int> GetPendingJobs()
    {

        var counter = await _context.Set<Job>()
            .Where(x => x.ProcessedTime == null)
            .CountAsync();

        return counter;
    }

    public async Task<int> GetScheduledJobs()
    {

        var counter = await _context.Set<Job>()
            .Where(x => x.ProcessedTime == null)
            .Where(x => x.ScheduleTime != null)
            .CountAsync();

        return counter;
    }

    public async Task<int> GetCreatedJobs()
    {
        var counter = await _context.Set<Job>()
            .Where(x => x.CurrentState == State.Created)
            .CountAsync();

        return counter;
    }

    public async Task<int> GetCompletedJobs()
    {
        var counter = await _context.Set<Job>()
            .Where(x => x.CurrentState == State.Completed)
            .CountAsync();

        return counter;
    }

    public async Task<int> GetFailedJobs()
    {
        var counter = await _context.Set<Job>()
            .Where(x => x.CurrentState == State.Failed)
            .CountAsync();

        return counter;
    }
}

public interface IHandfireService
{
    Task<int> GetPendingJobs();

    Task<int> GetTotalJobs();

    Task<int> GetScheduledJobs();

    Task<int> GetCreatedJobs();

    Task<int> GetCompletedJobs();

    Task<int> GetFailedJobs();
}
