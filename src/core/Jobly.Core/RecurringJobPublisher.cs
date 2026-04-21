using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Cronos;
using Jobly.Core.Data.Entities;
using Jobly.Core.Handlers;
using Microsoft.EntityFrameworkCore;

namespace Jobly.Core;

public interface IRecurringJobPublisher
{
    /// <summary>
    /// Registers or updates a recurring job definition. Does not create job instances — that is
    /// handled by <c>RecurringJobScheduler</c>. Acquires a distributed lock on the job name
    /// and saves changes immediately (callers should NOT call SaveChanges after this method).
    /// </summary>
    Task AddOrUpdateRecurringJob<T>(T message, string name, string cron)
        where T : class, IJob;
}

file static class RecurringJobPublisherConstants
{
    public static readonly char[] SplitChars = [' ', '\t'];
    public static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(15);
}

public class RecurringJobPublisher<TContext> : IRecurringJobPublisher
    where TContext : DbContext
{
    private readonly TContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly IJoblyLockProvider _lockProvider;

    public RecurringJobPublisher(TContext context, TimeProvider timeProvider, IJoblyLockProvider lockProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
        _lockProvider = lockProvider;
    }

    public async Task AddOrUpdateRecurringJob<T>(T message, string name, string cron)
        where T : class, IJob
    {
        ValidateCronExpression(cron);

        var handle = await _lockProvider.TryAcquireAsync($"jobly:recurring:{name}", RecurringJobPublisherConstants.LockTimeout, CancellationToken.None);
        if (handle == null)
        {
            throw new TimeoutException($"Could not acquire lock for recurring job '{name}' within {RecurringJobPublisherConstants.LockTimeout.TotalSeconds}s.");
        }

        await using (handle)
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var nextExecution = CronExpression.Parse(cron).GetNextOccurrence(now);
            var jobMessage = JsonSerializer.Serialize(message);
            var jobType = message.GetType().AssemblyQualifiedName!;

            var recurringJob = await _context.Set<RecurringJob>()
                .Where(x => x.Name == name)
                .FirstOrDefaultAsync();

            if (recurringJob != null)
            {
                recurringJob.Cron = cron;
                recurringJob.Message = jobMessage;
                recurringJob.Type = jobType;
                recurringJob.UpdatedAt = now;
                recurringJob.NextExecution = nextExecution;
            }
            else
            {
                recurringJob = new RecurringJob
                {
                    Name = name,
                    Message = jobMessage,
                    Type = jobType,
                    Cron = cron,
                    CreatedAt = now,
                    NextExecution = nextExecution,
                };

                await _context.Set<RecurringJob>().AddAsync(recurringJob);
            }

            await _context.SaveChangesAsync();
        }
    }

    private static void ValidateCronExpression(string cronExpression)
    {
        try
        {
            ParseCronExpression(cronExpression);
        }
        catch (Exception ex)
        {
            throw new ArgumentException(
                "CRON expression is invalid. Please see the inner exception for details.",
                nameof(cronExpression),
                ex);
        }
    }

    private static void ParseCronExpression([NotNull] string cronExpression)
    {
        ArgumentNullException.ThrowIfNull(cronExpression);

        var parts = cronExpression.Split(RecurringJobPublisherConstants.SplitChars, StringSplitOptions.RemoveEmptyEntries);
        var format = CronFormat.Standard;

        if (parts.Length == 6)
        {
            format |= CronFormat.IncludeSeconds;
        }
        else if (parts.Length != 5)
        {
            throw new CronFormatException(
                $"Wrong number of parts in the `{cronExpression}` cron expression, you can only use 5 or 6 (with seconds) part-based expressions.");
        }

        CronExpression.Parse(cronExpression, format);
    }
}
