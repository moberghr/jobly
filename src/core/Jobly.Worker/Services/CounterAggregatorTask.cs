using Jobly.Core;
using Jobly.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobly.Worker.Services;

public class CounterAggregatorTask<TContext> : ServerTaskBase<TContext>
    where TContext : DbContext
{
    public CounterAggregatorTask(
        IServiceScopeFactory scopeFactory,
        ILogger<CounterAggregatorTask<TContext>> logger,
        IOptions<JoblyWorkerConfiguration> configuration,
        IJoblyLockProvider lockProvider,
        TimeProvider timeProvider)
        : base(scopeFactory, logger, configuration, timeProvider, "jobly:counter-aggregation", lockProvider)
    {
    }

    protected override string TaskName => "AggregateCounters";

    protected override bool RerunImmediately => false;

    protected override TimeSpan DefaultInterval => Configuration.CounterAggregationInterval;

    protected override async Task<string?> RunServerTask(TContext context, CancellationToken ct)
    {
        var count = await AggregateCounters(context);
        return count > 0 ? $"Aggregated {count} counter rows" : null;
    }

    /// <summary>
    /// Aggregates pending Counter rows into the Statistic table.
    /// Public static so tests can call it directly.
    /// </summary>
    public static async Task<int> AggregateCounters<TCtx>(TCtx context)
        where TCtx : DbContext
    {
        var counters = await context.Set<Counter>().ToListAsync();

        if (counters.Count == 0)
        {
            return 0;
        }

        var grouped = counters
            .GroupBy(c => c.Key)
            .Select(g => new { Key = g.Key, Sum = g.Sum(c => c.Value) });

        foreach (var group in grouped)
        {
            var stat = await context.Set<Statistic>().FindAsync(group.Key);
            if (stat != null)
            {
                stat.Value += group.Sum;
            }
            else
            {
                context.Set<Statistic>().Add(new Statistic { Key = group.Key, Value = group.Sum });
            }
        }

        context.Set<Counter>().RemoveRange(counters);
        await context.SaveChangesAsync();

        return counters.Count;
    }
}
