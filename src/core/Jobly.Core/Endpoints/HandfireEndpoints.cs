using Jobly.Core.Enums;
using Jobly.Core.Models;
using Microsoft.AspNetCore.Builder;

namespace Jobly.Core.Endpoints;

public static class JoblyEndpoints
{
    public static void MapJoblyApiEndpoints(this WebApplication app, JoblyUIOptions options)
    {
        var aurCardsGroup = app.MapGroup($"{options.RoutePrefix}/api");

        aurCardsGroup.MapGet("status", async (IJoblyService handfireService) =>
        {
            var total = await handfireService.GetTotalJobsCount();
            var pending = await handfireService.GetPendingJobsCount();
            var scheduled = await handfireService.GetScheduledJobsCount();
            var created = await handfireService.GetJobsCount(State.Enqueued);
            var completed = await handfireService.GetJobsCount(State.Completed);
            var failed = await handfireService.GetJobsCount(State.Failed);

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
