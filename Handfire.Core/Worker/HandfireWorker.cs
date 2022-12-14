using System.Text.Json;
using Handfire.Core.Entities;
using Handfire.Core.Enums;
using Handfire.Core.Interceptors;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Handfire.Core.Worker;

public class HandfireWorker<TContext> : BackgroundService
    where TContext : DbContext
{
    public static int Counter = 0;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<HandfireWorker<TContext>> _logger;
    private readonly string _workerId = Guid.NewGuid().ToString();

    public HandfireWorker(IServiceScopeFactory serviceScopeFactory, ILogger<HandfireWorker<TContext>> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _serviceScopeFactory.CreateScope();

            var context = scope.ServiceProvider.GetRequiredService<TContext>();

            using var transaction = await context.Database.BeginTransactionAsync();

            var job = await context.Set<Job>()
                .Where(x => 
                    (x.ProcessedTime == null
                        && (x.ScheduleTime < DateTime.UtcNow || x.ScheduleTime == null)
                        && x.CurrentState == State.Created)
                    || x.CurrentState == State.Retry)
                .TagWith(ForUpdateSkipLockedCommandInterceptor.Label)
                .AsNoTracking()
                .FirstOrDefaultAsync();

            // if we didn't find any messages then we wait, otherwise we query again immediately 
            if (job == null)
            {
                await Task.Delay(1000);

                continue;
            }

            Interlocked.Increment(ref Counter);

            _logger.LogInformation("Worker {workerId} fetched message {id}", _workerId, job.Id);

            try
            {
                await ProcessOutboxMessage(job);
            }
            catch (Exception e)
            {
                await UpdateJobData(context, job, State.Failed, e.Message);

                transaction.Commit();

                continue;
            }

            await UpdateJobData(context, job, State.Completed);

            transaction.Commit();
        }
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

