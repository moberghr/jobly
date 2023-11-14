using Jobly.Core.Enums;
using Jobly.Core.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;

namespace Jobly.Core.Endpoints;

public static class JoblyEndpoints
{
    public static void MapJoblyApiEndpoints(this WebApplication app, JoblyUIOptions options)
    {
        var apiGroup = app.MapGroup($"{options.RoutePrefix}/api");

        apiGroup.MapGet("status", async ([FromServices] IJoblyService joblyService) =>
        {
            var total = await joblyService.GetTotalJobsCount();
            var pending = await joblyService.GetPendingJobsCount();
            var scheduled = await joblyService.GetScheduledJobsCount();
            var created = await joblyService.GetJobsCount(State.Enqueued);
            var completed = await joblyService.GetJobsCount(State.Completed);
            var failed = await joblyService.GetJobsCount(State.Failed);
            var processing = await joblyService.CountProcessingJobs() - completed - failed;

            var model = new DashboardStatistics
            {
                Total = total,
                Pending = pending,
                Scheduled = scheduled,
                Created = created,
                Completed = completed,
                Failed = failed,
                Processing = processing
            };

            return model;
        });

        apiGroup.MapGet("details", async ([FromServices] IJoblyService joblyService, [FromBody] JobStateRequest request) =>
        {
            var model = await joblyService.GetJobStates(request);
            return model;
        });

        apiGroup.MapGet("created", async ([FromServices] IJoblyService joblyService, [FromBody] BaseListRequest request) =>
        { 
            var model = await joblyService.GetJobsList(request, State.Enqueued); 
            return model;
        });

        apiGroup.MapGet("completed", async ([FromServices] IJoblyService joblyService, [FromBody] BaseListRequest request) =>
        { 
            var model = await joblyService.GetJobsList(request, State.Completed); 
            return model;
        });

        apiGroup.MapGet("failed", async ([FromServices] IJoblyService joblyService, [FromBody] BaseListRequest request) =>
        { 
            var model = await joblyService.GetJobsList(request, State.Failed); 
            return model;
        });

        apiGroup.MapGet("processing", async ([FromServices] IJoblyService joblyService, [FromBody] BaseListRequest request) =>
        { 
            var model = await joblyService.GetJobStatesInProcess(request); 
            return model;
        });

        apiGroup.MapPost("retry", async ([FromServices] IJoblyService joblyService, [FromQuery] string jobId) =>
        {
            await joblyService.SetRetry(jobId);
        });

        apiGroup.MapGet("scheduled", async ([FromServices] IJoblyService joblyService, [FromBody] BaseListRequest request) =>
        {
            var model = await joblyService.GetScheduledJobs(request); 
            return model;
        });
    }
}
