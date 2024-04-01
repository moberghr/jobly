using Jobly.Core.Enums;

namespace Jobly.Worker.Interceptors;

public class RetryInterceptor : JobInterceptor
{
    public override Task JobExecutionFailedAsync(JobExecutingContext context, CancellationToken cancellationToken)
    {
        // If the job has not reached the maximum number of retries, enqueue the job again.
        if (context.Job.RetriedTimes < context.Job.MaxRetries)
        {
            context.Job.CurrentState = State.Enqueued;
            context.Job.RetriedTimes += 1;
            return Task.CompletedTask;
        }

        context.Job.CurrentState = State.Failed;
        return Task.CompletedTask;
    }
}