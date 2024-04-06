using Jobly.Worker.Interceptors;

namespace Jobly.Test.App;

public class LoggingInterceptor : JobInterceptor
{
    private DateTime _startTime;

    public LoggingInterceptor()
    {
        Console.WriteLine("LoggingInterceptor created.");
    }

    public override Task JobWillExecuteAsync(JobExecutingContext context, CancellationToken cancellationToken)
    {
        // Console.WriteLine($"Job {context.Job.Id} is about to execute.");
        _startTime = DateTime.Now;
        return Task.CompletedTask;
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