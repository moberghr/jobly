using Jobly.Worker.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace Jobly.Test.App;

public class LoggingInterceptor : JobInterceptor
{
    private DateTime _startTime;

    public LoggingInterceptor()
    {
        Console.WriteLine("LoggingInterceptor created.");
    }

    public override Task<InterceptionResult> JobWillExecuteAsync(JobExecutingContext context, InterceptionResult result, CancellationToken cancellationToken)
    {
        // Console.WriteLine($"Job {context.Job.Id} is about to execute.");
        _startTime = DateTime.Now;
        return Task.FromResult(result);
    }

    public override Task JobExecutionFailedAsync(JobExecutingContext context, CancellationToken cancellationToken)
    {
        
        Console.WriteLine($"Job {context.Job.Id} failed to execute. Elapsed time: {DateTime.Now - _startTime}");
        return Task.CompletedTask;
    }

    public override Task JobExecutedAsync(JobExecutingContext context, CancellationToken cancellationToken)
    {
        
        Console.WriteLine($"Job {context.Job.Id} executed successfully. Elapsed time: {DateTime.Now - _startTime}");
        return Task.CompletedTask;
    }
}