using Handfire.Core.Enums;
using Handfire.Core.Models;
using Microsoft.AspNetCore.Builder;

namespace Handfire.Core.Endpoints;

public static class HandfireEndpoints
{
    public static void MapHandfireApiEndpoints(this WebApplication app, HandfireUIOptions options)
    {
        var aurCardsGroup = app.MapGroup($"{options.RoutePrefix}/api");

        aurCardsGroup.MapGet("status", async (IHandfireService handfireService) =>
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
