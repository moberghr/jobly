using Microsoft.EntityFrameworkCore;

namespace Jobly.Core;

public class PostgresNotifyNotifyProvider<TContext> : IJoblyNotifer where TContext : DbContext
{
    private readonly TContext _context;

    public PostgresNotifyNotifyProvider(TContext context)
    {
        _context = context;
    }

    public async Task NotifyAsync(CancellationToken cancellationToken = default)
    {
        await _context.Database.ExecuteSqlRawAsync("NOTIFY job_added;", cancellationToken: cancellationToken);        
    }
}