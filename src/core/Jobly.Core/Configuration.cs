using Jobly.Core.Enums;

namespace Jobly.Core;

public class JoblyConfiguration
{
    public int RetryCount { get; set; }

    public Priority DefaultBatchPriority { get; set; } = Priority.Normal;
}
