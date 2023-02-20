using System.Text.Json;
using Cronos;
using Handfire.Core.Data.Entities;
using Handfire.Core.Entities;
using Handfire.Core.Enums;
using Handfire.Core.Interceptors;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Handfire.Core;

public static class JobQueryHelper
{
    public static IQueryable<Job> GetJobs<TContext>(this TContext context) where TContext : DbContext
    {
        return context.Set<Job>()
                .WhereIsPendingOrRetry()
                .TagWith(InterceptorConstants.RowLock)
                .AsNoTracking();
    }

    private static IQueryable<Job> WhereIsPendingOrRetry(this IQueryable<Job> query)
    {
        return query
            .Where(x => x.CurrentState == State.Enqueued)
            .Where(x => x.ScheduleTime < DateTime.UtcNow || x.ScheduleTime == null);
    }
}

public interface IHandfireWorkerService
{
    Task GetAndProcessJob(CancellationToken cancellationToken);
}

public class HandfireWorkerService<TContext> : IHandfireWorkerService
    where TContext : DbContext
{
    private readonly string _workerId = Guid.NewGuid().ToString();
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<HandfireWorkerService<TContext>> _logger;

    public HandfireWorkerService(IServiceScopeFactory serviceScopeFactory, ILogger<HandfireWorkerService<TContext>> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
    }

    public async Task GetAndProcessJob(CancellationToken cancellationToken)
    {
        using var scope = _serviceScopeFactory.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var job = await context.GetJobs()
            .FirstOrDefaultAsync(cancellationToken);

        // if we didn't find any messages then we wait, otherwise we query again immediately 
        if (job == null)
        {
            await Task.Delay(1000, cancellationToken);

            return;
        }

        _logger.LogInformation("Worker {workerId} fetched message {id}", _workerId, job.Id);

        try
        {
            if (job.RecurringJobId.HasValue)
            {
                await CreateNextJob(job, cancellationToken);
            }
            await ProcessOutboxMessage(job, cancellationToken);
        }
        catch (Exception e)
        {
            await UpdateJobData(context, job, e.Message, cancellationToken);

            transaction.Commit();

            return;
        }

        await UpdateJobData(context, job, message: null, cancellationToken);
        transaction.Commit();
    }

    private async Task CreateNextJob(Job job, CancellationToken cancellationToken)
    {
        using var scope = _serviceScopeFactory.CreateScope();

        var temporaryContext = scope.ServiceProvider.GetRequiredService<TContext>();

        var recurringJob = await temporaryContext.Set<RecurringJob>()
            .Where(x => x.Id == job.RecurringJobId)
            .FirstAsync(cancellationToken);

        if (recurringJob.NextJobId != job.Id)
        {
            return;
        }

        var createTime = DateTime.UtcNow;

        var fromUtc = DateTime.SpecifyKind(recurringJob.NextExecution ?? DateTime.UtcNow, DateTimeKind.Utc);
        var nextJobScheduleTime = CronExpression.Parse(recurringJob.Cron).GetNextOccurrence(fromUtc);

        var jobStats = new List<JobState>
        {
            new() { State = State.Enqueued, DateTime = createTime}
        };

        var newJobId = Guid.NewGuid().ToString();
        var newJob = new Job
        {
            Id = newJobId,
            Message = recurringJob.Message,
            Type = recurringJob.Type,
            CreateTime = createTime,
            ScheduleTime = nextJobScheduleTime,
            CurrentState = State.Enqueued,
            RecurringJobId = recurringJob.Id,
            JobStates = jobStats
        };

        recurringJob.LastExecution = recurringJob.NextExecution;
        recurringJob.LastJobId = recurringJob.NextJobId;

        recurringJob.NextExecution = nextJobScheduleTime;
        recurringJob.NextJob = newJob;

        await temporaryContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ProcessOutboxMessage(Job message, CancellationToken cancellationToken)
    {
        var type = Type.GetType(message.Type);

        if (type is null)
        {
            throw new HandfireException($"Unknown type {message.Type}");
        }

        var request = JsonSerializer.Deserialize(message.Message, type);

        if (request is null)
        {
            throw new HandfireException($"Unable to deserialize message {message.Message} to type {message.Type}");
        }

        using var scope = _serviceScopeFactory.CreateScope();

        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await mediator.Send(request, cancellationToken);
    }

    private static async Task UpdateJobData(TContext context, Job job, string? message, CancellationToken cancellationToken)
    {
        var state = !string.IsNullOrEmpty(message) ? State.Failed : State.Completed;
        if (job.RetriedTimes < job.MaxRetries && !string.IsNullOrEmpty(message))
        {
            state = State.Enqueued;
            job.RetriedTimes += 1;
        }

        UpdateJob(context, job, state);

        UpdateChildJobs(context, job.Id, state);
        
        await CreateJobState(context, job.Id, state, message, cancellationToken);
    }

    private static void UpdateJob(TContext context, Job job, State state)
    {
        job.CurrentState = state;

        context.Set<Job>().Update(job);
    }

    private static async Task CreateJobState(TContext context, string jobId, State state, string? message, CancellationToken cancellationToken)
    {
        var jobState = new JobState
        {
            JobId = jobId,
            DateTime = DateTime.UtcNow,
            State = state,
            Message = message
        };

        await context.Set<JobState>().AddAsync(jobState, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static void UpdateChildJobs(TContext context, string parentJobId, State state)
    {
        var jobs = context.Set<Job>()
            .Where(x => x.ParentJobId == parentJobId)
            .Where(x => x.CurrentState == State.Awaiting)
            .AsQueryable();

        if (jobs.Any())
        {
            foreach (var job in jobs)
            {
                job.CurrentState = state == State.Completed ? State.Enqueued : State.Failed;
            }
        }
    }
}