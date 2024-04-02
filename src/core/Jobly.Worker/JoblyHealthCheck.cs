using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Jobly.Worker;

public class JoblyHealthCheck : IHealthCheck 
{
    private readonly IJoblyWorkerScheduler _joblyWorkerScheduler;

    public JoblyHealthCheck(IJoblyWorkerScheduler joblyWorkerScheduler)
    {
        _joblyWorkerScheduler = joblyWorkerScheduler;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var isHealthy = _joblyWorkerScheduler.IsHealthy;

        return Task.FromResult(isHealthy ? HealthCheckResult.Healthy("The background service is running healthy.") : HealthCheckResult.Unhealthy("The background service is not running healthy."));
    }
}