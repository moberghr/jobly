namespace Jobly.Core.Handlers;

/// <summary>
/// Declares retry policy for a job or handler. Can be applied to IJob or IJobHandler implementations.
/// Priority: per-enqueue metadata override > handler attribute > job attribute > global RetryOptions.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RetryAttribute : Attribute
{
    public RetryAttribute(int maxRetries)
    {
        MaxRetries = maxRetries;
    }

    public int MaxRetries { get; }

    public int[]? Delays { get; set; }
}
