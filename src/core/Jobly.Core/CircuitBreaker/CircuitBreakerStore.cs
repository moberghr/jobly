using Jobly.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jobly.Core.CircuitBreaker;

public class CircuitBreakerStore<TContext> : ICircuitBreakerStore
    where TContext : DbContext
{
    private readonly TContext _context;
    private readonly IServiceScopeFactory _scopeFactory;

    public CircuitBreakerStore(TContext context, IServiceScopeFactory scopeFactory)
    {
        _context = context;
        _scopeFactory = scopeFactory;
    }

    public Task<CircuitBreakerState?> GetAsync(string groupKey, CancellationToken cancellationToken)
    {
        return _context.Set<CircuitBreakerState>()
            .AsNoTracking()
            .Where(x => x.GroupKey == groupKey)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task ResetAsync(string groupKey, CancellationToken cancellationToken)
    {
        await _context.Set<CircuitBreakerState>()
            .Where(x => x.GroupKey == groupKey)
            .Where(x => x.FailureCount > 0)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(x => x.FailureCount, 0)
                    .SetProperty(x => x.OpenUntil, (DateTime?)null),
                cancellationToken);
    }

    public async Task RecordFailureAsync(string groupKey, int threshold, TimeSpan duration, DateTime now, CancellationToken cancellationToken)
    {
        var openUntilIfThreshold = now + duration;
        var affected = await _context.Set<CircuitBreakerState>()
            .Where(x => x.GroupKey == groupKey)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(x => x.FailureCount, x => x.FailureCount + 1)
                    .SetProperty(x => x.LastFailureAt, now)
                    .SetProperty(x => x.OpenUntil, x => x.FailureCount + 1 >= threshold ? openUntilIfThreshold : x.OpenUntil),
                cancellationToken);

        if (affected > 0)
        {
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var insertContext = scope.ServiceProvider.GetRequiredService<TContext>();
        try
        {
            insertContext.Set<CircuitBreakerState>().Add(
                new CircuitBreakerState
                {
                    GroupKey = groupKey,
                    FailureCount = 1,
                    LastFailureAt = now,
                    OpenUntil = threshold <= 1 ? openUntilIfThreshold : null,
                });
            await insertContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            var rowExists = await _context.Set<CircuitBreakerState>()
                .AsNoTracking()
                .Where(x => x.GroupKey == groupKey)
                .AnyAsync(cancellationToken);
            if (!rowExists)
            {
                throw;
            }

            await _context.Set<CircuitBreakerState>()
                .Where(x => x.GroupKey == groupKey)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(x => x.FailureCount, x => x.FailureCount + 1)
                        .SetProperty(x => x.LastFailureAt, now)
                        .SetProperty(x => x.OpenUntil, x => x.FailureCount + 1 >= threshold ? openUntilIfThreshold : x.OpenUntil),
                    cancellationToken);
        }
    }
}
