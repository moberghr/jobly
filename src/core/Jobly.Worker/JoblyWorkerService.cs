using System.Text.Json;
using Cronos;
using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Interceptors;
using Jobly.Worker.Interceptors;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobly.Worker;

public interface IJoblyWorkerService
{
    Task<bool> GetAndProcessJob(CancellationToken cancellationToken);
}

public class JoblyWorkerService<TContext> : IJoblyWorkerService
    where TContext : DbContext
{
    private readonly Guid _workerId = Guid.NewGuid();
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<JoblyWorkerService<TContext>> _logger;
    private readonly JoblyWorkerConfiguration _configuration;
    private readonly IInterceptorService _interceptorService;
    
    public JoblyWorkerService(IServiceScopeFactory serviceScopeFactory, ILogger<JoblyWorkerService<TContext>> logger,
        IOptions<JoblyWorkerConfiguration> configuration, IInterceptorService interceptorService)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
        _interceptorService = interceptorService;
        _configuration = configuration.Value;
    }

    private void UpdateJobStatusToProcessing(TContext context, Job job)
    {
        var jobState = new JobState
        {
            JobId = job.Id,
            DateTime = DateTime.UtcNow,
            State = State.Processing,
            Message = $"The job {job.Id} is being processed"
        };

        job.CurrentState = State.Processing;
        job.CurrentServerId = _configuration.ServerId;
        job.CurrentWorkerId = _workerId;

        context.Set<JobState>().Add(jobState);
    }

    public async Task<bool> GetAndProcessJob(CancellationToken cancellationToken)
    {
        using var scope = _serviceScopeFactory.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var job = context.Set<Job>()
            .Where(x => x.CurrentState == State.Enqueued)
            .Where(x => x.ScheduleTime < DateTime.UtcNow)
            .OrderBy(x => x.Priority)
            .ThenBy(x => x.ScheduleTime)
            .TagWith(InterceptorConstants.RowLockTableJob)
            .FirstOrDefault();

        // if we didn't find any messages then we wait, otherwise we query again immediately 
        if (job == null)
        {
            await transaction.CommitAsync(cancellationToken);

            return false;
        }

        _logger.LogInformation("Worker {workerId} fetched message {id}", _workerId, job.Id);

        UpdateJobStatusToProcessing(context, job);

        var executingContext = _interceptorService.CreateInterceptorPipeline(context, job, scope);
        await _interceptorService.RunWillExecuteInterceptors(executingContext, cancellationToken);

        // Saving the job in processing state so that it is marked as processing in the db.
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        if (executingContext.IsSuppressed)
        {
            // todo: set some state here
            return true;
        }

        try
        {
            // Processing the message, we don't want to keep a transaction open during this time, it may take a while
            // and we don't want to keep a db lock open for that long. There is also no need to rollback the transaction
            // since we are not doing any db operations here. The ProcessOutboxMessage has its own scope anyway.

            await ProcessOutboxMessage(job, cancellationToken);

            await using var endTransaction = await context.Database.BeginTransactionAsync(default);

            await UpdateJobData(context, job, message: null, default);

            await _interceptorService.RunJobExecutedInterceptors(executingContext, cancellationToken);

            await context.SaveChangesAsync(default);
            await endTransaction.CommitAsync(default);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error processing message {id}", job.Id);

            await using var endTransaction = await context.Database.BeginTransactionAsync(default);

            await UpdateJobData(context, job, e.Message, default);

            await _interceptorService.RunJobExecutionFailedInterceptors(executingContext, cancellationToken);

            await context.SaveChangesAsync(default);
            await endTransaction.CommitAsync(default);
        }

        return true;
    }

    private async Task ProcessOutboxMessage(Job message, CancellationToken cancellationToken)
    {
        var type = Type.GetType(message.Type);

        if (type is null)
        {
            throw new JoblyException($"Unknown type {message.Type}");
        }

        var request = JsonSerializer.Deserialize(message.Message, type);

        if (request is null)
        {
            throw new JoblyException($"Unable to deserialize message {message.Message} to type {message.Type}");
        }

        using var scope = _serviceScopeFactory.CreateScope();

        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await mediator.Send(request, cancellationToken);
    }

    private static async Task UpdateJobData(TContext context, Job job, string? message,
        CancellationToken cancellationToken)
    {
        var state = !string.IsNullOrEmpty(message) ? State.Failed : State.Completed;

        job.CurrentState = state;

        await CreateJobState(context, job.Id, state,
            string.IsNullOrEmpty(message) ? $"Job {job.Id} is completed" : message, cancellationToken);
    }

    private static async Task CreateJobState(TContext context, Guid jobId, State state, string? message,
        CancellationToken cancellationToken)
    {
        var jobState = new JobState
        {
            JobId = jobId,
            DateTime = DateTime.UtcNow,
            State = state,
            Message = message
        };

        await context.Set<JobState>().AddAsync(jobState, cancellationToken);
    }
}