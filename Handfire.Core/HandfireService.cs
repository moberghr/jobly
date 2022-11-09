using Handfire.Core.Entities;
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

        var counter = await _context.Set<OutboxMessage>()
            .CountAsync();

        return counter;
    }

    public async Task<int> GetPendingJobs()
    {

        var counter = await _context.Set<OutboxMessage>()
            .Where(x => x.ProcessedTime == null)
            .CountAsync();

        return counter;
    }
}

public interface IHandfireService
{
    Task<int> GetPendingJobs();

    Task<int> GetTotalJobs();
}
