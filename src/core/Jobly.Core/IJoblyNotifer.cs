using Jobly.Core.Entities;

namespace Jobly.Core;

/// <summary>
/// IJoblyNotifer will be used to notify the worker that a job has been added to the queue
/// </summary>
public interface IJoblyNotifer
{
    Task NotifyAsync(Job job, CancellationToken cancellationToken = default);
}