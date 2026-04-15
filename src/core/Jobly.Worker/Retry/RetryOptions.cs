namespace Jobly.Worker.Retry;

public class RetryOptions
{
    public int MaxRetries { get; set; }

    public int[] Delays { get; set; } = [15, 60, 300];
}
