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

file static class JobQueryHelper
{
    public static IQueryable<Job> GetJobs<TContext>(this TContext context) where TContext : DbContext
    {
        return context.Set<Job>()
                .WhereIsPendingOrRetry()
                .TagWith(ForUpdateSkipLockedCommandInterceptor.Label)
                .AsNoTracking();
    }

    private static IQueryable<Job> WhereIsPendingOrRetry(this IQueryable<Job> query)
    {
        return query
            .Where(x =>
                (x.ProcessedTime == null
                    && (x.ScheduleTime < DateTime.UtcNow || x.ScheduleTime == null)
                    && x.CurrentState == State.Created)
                || x.CurrentState == State.Retry);
    }
}

public interface IHandfireWorkerService
{
    Task GetAndProcessJob(string workerId);
}

public class HandfireWorkerService<TContext> : IHandfireWorkerService
    where TContext : DbContext
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<HandfireWorkerService<TContext>> _logger;

    public HandfireWorkerService(IServiceScopeFactory serviceScopeFactory, ILogger<HandfireWorkerService<TContext>> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
    }

    public async Task GetAndProcessJob(string workerId)
    {
        using var scope = _serviceScopeFactory.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        using var transaction = await context.Database.BeginTransactionAsync();

        var job = await context.GetJobs()
            .FirstOrDefaultAsync();

        // if we didn't find any messages then we wait, otherwise we query again immediately 
        if (job == null)
        {
            await Task.Delay(1000);

            return;
        }

        _logger.LogInformation("Worker {workerId} fetched message {id}", workerId, job.Id);

        try
        {
            if (job.RecurringJobId.HasValue)
            {
                await CreateNextJob(job);
            }

            await ProcessOutboxMessage(job);
        }
        catch (Exception e)
        {
            await UpdateJobData(context, job, State.Failed, e.Message);

            transaction.Commit();

            return;
        }

        await UpdateJobData(context, job, State.Completed);
        transaction.Commit();
    }

    private async Task CreateNextJob(Job job)
    {
        using var scope = _serviceScopeFactory.CreateScope();

        var temporaryContext = scope.ServiceProvider.GetRequiredService<TContext>();

        var recurringJob = await temporaryContext.Set<RecurringJob>()
            .Where(x => x.Id == job.RecurringJobId)
            .FirstAsync();

        if (recurringJob.NextJobId != job.Id)
        {
            return;
        }

        var createTime = DateTime.UtcNow;

        var nextJobScheduleTime = CronExpression.Parse(recurringJob.Cron).GetNextOccurrence(recurringJob.NextExecution ?? DateTime.UtcNow);

        var jobStats = new List<JobState>
        {
            new() { State = State.Created, DateTime = createTime}
        };

        var newJob = new Job
        {
            Message = recurringJob.Message,
            Type = recurringJob.Type,
            CreateTime = createTime,
            ScheduleTime = nextJobScheduleTime,
            CurrentState = State.Created,
            RecurringJobId = recurringJob.Id,
            JobStates = jobStats
        };

        recurringJob.LastExecution = recurringJob.NextExecution;
        recurringJob.LastJobId = recurringJob.NextJobId;

        recurringJob.NextExecution = nextJobScheduleTime;
        recurringJob.NextJob = newJob;

        await temporaryContext.SaveChangesAsync();
    }

    private async Task ProcessOutboxMessage(Job message)
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

        await mediator.Send(request);
    }

    private static async Task UpdateJobData(TContext context, Job job, State state, string? message = null)
    {
        UpdateJob(context, job, state);

        await CreateJobState(context, job.Id, state, message);
    }

    private static void UpdateJob(TContext context, Job job, State state)
    {
        job.CurrentState = state;
        job.ProcessedTime = DateTime.UtcNow;

        context.Set<Job>().Update(job);
    }

    private static async Task CreateJobState(TContext context, int jobId, State state, string? message = null)
    {
        var jobState = new JobState
        {
            JobId = jobId,
            DateTime = DateTime.UtcNow,
            State = state,
            Message = message
        };

        await context.Set<JobState>().AddAsync(jobState);
        await context.SaveChangesAsync();
    }
}