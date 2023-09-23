using Jobly.Core.Enums;
using Jobly.Core.Models;
using Microsoft.AspNetCore.Builder;

namespace Jobly.Core.Endpoints;

public static class JoblyEndpoints
{
    public static void MapJoblyApiEndpoints(this WebApplication app, JoblyUIOptions options)
    {
        var aurCardsGroup = app.MapGroup($"{options.RoutePrefix}/api");

        aurCardsGroup.MapGet("status", async (IJoblyService joblyService) =>
        {
            var total = await joblyService.GetTotalJobsCount();
            var pending = await joblyService.GetPendingJobsCount();
            var scheduled = await joblyService.GetScheduledJobsCount();
            var created = await joblyService.GetJobsCount(State.Enqueued);
            var completed = await joblyService.GetJobsCount(State.Completed);
            var failed = await joblyService.GetJobsCount(State.Failed);

            var model = new DashboardStatistics
            {
                Total = total,
                Pending = pending,
                Scheduled = scheduled,
                Created = created,
                Completed = completed,
                Failed = failed
            };

            return model;
        });
    }
}
