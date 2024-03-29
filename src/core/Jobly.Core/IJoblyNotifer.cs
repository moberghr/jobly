namespace Jobly.Core;

/// <summary>
/// IJoblyNotifer will be used to notify the worker that a job has been added to the queue
/// </summary>
public interface IJoblyNotifer
{
    Task NotifyAsync(CancellationToken cancellationToken = default);
}