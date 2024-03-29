using Jobly.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Jobly.Core;

public class PostgresNotifyNotifyProvider<TContext> : IJoblyNotifer where TContext : DbContext
{
    private readonly TContext _context;

    public PostgresNotifyNotifyProvider(TContext context)
    {
        _context = context;
    }

    public async Task NotifyAsync(Job job, CancellationToken cancellationToken = default)
    {
        await _context.Database.ExecuteSqlRawAsync($"select pg_notify('job_added', {job.Id};", cancellationToken: cancellationToken);        
    }
}