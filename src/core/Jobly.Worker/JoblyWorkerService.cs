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
    Task GetAndProcessJobs(Guid serverId, CancellationToken cancellationToken);
    
    Task<bool> GetAndProcessJob(CancellationToken cancellationToken);
}

public class JoblyWorkerService<TContext> : IJoblyWorkerService
    where TContext : DbContext
{
    private readonly Guid _workerId = Guid.NewGuid();
    
    // todo: get this id from another pattern, maybe in a configuration? 
    // temporary solution to get the server id
    private Guid? _serverId;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<JoblyWorkerService<TContext>> _logger;
    private readonly IConfigureOptions<JoblyWorkerConfiguration> _configuration;
    

    public JoblyWorkerService(IServiceScopeFactory serviceScopeFactory,
        ILogger<JoblyWorkerService<TContext>> logger,
        IConfigureOptions<JoblyWorkerConfiguration> configuration)
    {
        _configuration = configuration;
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Sets the state of the job to processing
    /// </summary>
    /// <param name="job">Job to set</param>
    private void UpdateJobStatusToProcessing(Job job)
    {
        // The diff here is that we set the job state to processing and add a message
        // in the same transaction as the job was read from the db.
        // it will be saved before the processing happens.
        
        // commenting out so we see the diff
        // using var scope = _serviceScopeFactory.CreateScope();
        // var context = scope.ServiceProvider.GetRequiredService<TContext>();

        var jobState = new JobState
        {
            JobId = job.Id,
            DateTime = DateTime.UtcNow,
            State = State.Processing,
            Message = $"The job {job.Id} is being processed"
        };
        
        job.CurrentState = State.Processing;
        job.CurrentServerId = _serverId;
        job.CurrentWorkerId = _workerId;;
        job.JobStates.Add(jobState);

        // commenting out so we see the diff
        // await context.Set<JobState>().AddAsync(jobState, cancellationToken);
        // await context.SaveChangesAsync(cancellationToken);
    }

    public async Task GetAndProcessJobs(Guid serverId, CancellationToken cancellationToken)
    {
        _serverId = serverId;
        var isJobProcessing = true;
        while (!cancellationToken.IsCancellationRequested && isJobProcessing)
        {   
            isJobProcessing = await GetAndProcessJob(cancellationToken);
        }
    }

    public async Task<bool> GetAndProcessJob(CancellationToken cancellationToken)
    {
        using var scope = _serviceScopeFactory.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        // This transaction is for finding jobs and setting that job to processing.
        // After it is in processing no other job should find it to process. But it is visible to the rest
        // of the system as processing. We need to clean up dead processing jobs, since it can be left orphaned.
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
            return false;
        }
        // _logger.LogInformation("Worker {workerId} fetched message {id}", _workerId, job.Id);
        
        // TODO: start the interceptor chain in a separate method
        var workerConfiguration = scope.ServiceProvider.GetRequiredService<IOptions<JoblyWorkerConfiguration>>().Value;
        var executingContext = new JobExecutingContext(job, context);
        var result = new InterceptionResult();
        
        var interceptors = workerConfiguration
            .Interceptors.Select(x => (IJobInterceptor)scope.ServiceProvider.GetRequiredService(x)).ToList();
        
        // Add default interceptors
        interceptors.Add(scope.ServiceProvider.GetRequiredService<RetryInterceptor>());
        interceptors.Add(scope.ServiceProvider.GetRequiredService<ContinuationInterceptor>());
        
        foreach (var interceptor in interceptors)
        {
            result = await interceptor.JobWillExecuteAsync(executingContext, result, cancellationToken);
            if (!result.IsSuppressed) continue;
            // todo: add cancel state
            await transaction.CommitAsync(cancellationToken);
            return false;
        }
        
        UpdateJobStatusToProcessing(job);
        
        // todo: this can be moved to an interceptor
        if (job.RecurringJobId.HasValue)
        {
            // todo: what if the job is rerunning
            await CreateNextJob(context, job, cancellationToken);
        }
        
        // Saving the job in processing state so that it is marked as processing in the db.
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        
        try
        {
            // Processing the message, we don't want to keep a transaction open during this time, it may take a while
            // and we don't want to keep a db lock open for that long. There is also no need to rollback the transaction
            // since we are not doing any db operations here. The ProcessOutboxMessage has its own scope anyway.
            await ProcessOutboxMessage(job, cancellationToken);
            
            // Entering the processing state
            await using var processingTransaction = await context.Database.BeginTransactionAsync(cancellationToken);
            
            await UpdateJobData(context, job, message: null, cancellationToken);
            
            // Trigger the JobExecuted interceptors
            foreach (var interceptor in interceptors)
            {
                await interceptor.JobExecutedAsync(executingContext, cancellationToken);
            }   
            
            await processingTransaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (Exception e)
        {
            await using var errorTransaction = await context.Database.BeginTransactionAsync(cancellationToken);
            await UpdateJobData(context, job, e.Message, cancellationToken);

            // Trigger the JobFailed interceptors
            foreach (var interceptor in interceptors)
            {
                await interceptor.JobExecutionFailedAsync(executingContext, cancellationToken);
            }   
            await errorTransaction.CommitAsync(cancellationToken);
            _logger.LogError(e, "Error processing job {jobId}", job.Id);
            
            return false;
        }
    }

    private async Task CreateNextJob(TContext context, Job job, CancellationToken cancellationToken)
    {
        var recurringJob = await context.Set<RecurringJob>()
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
            ScheduleTime = nextJobScheduleTime ?? createTime,
            Priority = job.Priority,
            CurrentState = State.Enqueued,
            RecurringJobId = recurringJob.Id,
            JobStates = jobStats
        };

        recurringJob.LastExecution = recurringJob.NextExecution;
        recurringJob.LastJobId = recurringJob.NextJobId;

        recurringJob.NextExecution = nextJobScheduleTime;
        recurringJob.NextJob = newJob;
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

    private static async Task UpdateJobData(TContext context, Job job, string? message, CancellationToken cancellationToken)
    {
        var state = !string.IsNullOrEmpty(message) ? State.Failed : State.Completed;

        job.CurrentState = state;

        await CreateJobState(context, job.Id, state, string.IsNullOrEmpty(message) ? $"Job {job.Id} is completed" : message, cancellationToken);

        if (job.BatchId != null)
        {
            await UpdateCurrentAndNextBatchFromChildJob(context, job.BatchId, cancellationToken);
        }

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

    private static async Task UpdateCurrentAndNextBatchFromChildJob(TContext context, string batchId, CancellationToken cancellationToken)
    {
        // // Decrease the counter of the batch // todo: I'm working on getting rid of the locking if possible
        // await context.Set<Batch>()
        //     .Where(x => x.Id == batchId)
        //     .ExecuteUpdateAsync(x => x.SetProperty(y => y.Counter, y => y.Counter - 1), cancellationToken);
        //
        // var count = await context.Set<Batch>()
        //     .Where(x => x.Id == batchId)
        //     .Select(x => x.Counter)
        //     .FirstOrDefaultAsync(cancellationToken);
        //
        // // If all jobs in a single batch are finished
        // if (count > 0)
        // {
        //     return;
        // }
        
        var currentBatch = await context.Set<Batch>()
            .Where(x => x.Id == batchId)
            .TagWith(InterceptorConstants.RowLockTableBatch)
            .FirstOrDefaultAsync(cancellationToken);

        // Check if this is a batch job
        if (currentBatch == null)
        {
            return;
        }

        currentBatch.Counter--;

        // If all jobs in a single batch are finished
        if (currentBatch.Counter > 0)
        {
            return;
        }

        currentBatch.Counter = 0;

        var currentBatchJob = await context.Set<Job>()
            .Where(x => x.Id == batchId)
            .FirstAsync(cancellationToken);

        currentBatchJob.CurrentState = State.Completed;

        
        // todo: how is this any different from the UpdateChildJobs method? Why dont we start next jobs there whether
        // they are in a batch or not?
        var nextBatchJob = await context.Set<Job>()
            .Where(x => x.ParentJobId == currentBatchJob.Id)
            .FirstOrDefaultAsync(cancellationToken);

        // Check if another parent job exists
        // If yes, then start another batch jobs process
        // if no, then no more jobs exists that need to be started (this is the last one)
        if (nextBatchJob == null)
        {
            return;
        }

        var nextBatch = await context.Set<Batch>()
            .Where(x => x.Id == nextBatchJob.Id)
            .FirstOrDefaultAsync(cancellationToken);

        // Check if this is another batch of jobs or...
        if (nextBatch != null)
        {
            var nextBatchJobs = await context.Set<Job>()
                .Where(x => x.BatchId == nextBatch.Id)
                .ToListAsync(cancellationToken);

            foreach (var batchJob in nextBatchJobs)
            {
                batchJob.CurrentState = State.Enqueued;
            }
        }
        // ...A single job
        else
        {
            nextBatchJob.CurrentState = State.Enqueued;
        }
    }
}