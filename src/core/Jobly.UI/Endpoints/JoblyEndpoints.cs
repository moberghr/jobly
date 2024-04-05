using Jobly.Core;
using Jobly.Core.Enums;
using Jobly.Core.Models;
using Jobly.UI.UIMiddleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jobly.UI.Endpoints;

public static class JoblyEndpoints
{
    public static void MapJoblyApiEndpoints(this WebApplication app, JoblyUIOptions options)
    {
        var apiGroup = app.MapGroup($"{options.RoutePrefix}/api");

        apiGroup.MapGet("status", async ([FromServices] IJoblyService joblyService) =>
        {
            var model = await joblyService.GetJoblyStatus();
            return model;
        });

        apiGroup.MapGet("details", async ([FromServices] IJoblyService joblyService, [AsParameters] JobStateRequest request) =>
        {
            var model = await joblyService.GetJobStates(request);
            return model;
        });

        apiGroup.MapGet("created", async ([FromServices] IJoblyService joblyService, [AsParameters] BaseListRequest request) =>
        {
            var model = await joblyService.GetJobsList(request, State.Enqueued);
            return model;
        });

        apiGroup.MapGet("completed", async ([FromServices] IJoblyService joblyService, [AsParameters] BaseListRequest request) =>
        {
            var model = await joblyService.GetJobsList(request, State.Completed);
            return model;
        });

        apiGroup.MapGet("failed", async ([FromServices] IJoblyService joblyService, [AsParameters] BaseListRequest request) =>
        {
            var model = await joblyService.GetJobsList(request, State.Failed);
            return model;
        });

        apiGroup.MapGet("processing", async ([FromServices] IJoblyService joblyService, [AsParameters] BaseListRequest request) =>
        {
            var model = await joblyService.GetJobStatesInProcess(request);
            return model;
        });

        apiGroup.MapPost("retry/{jobId}", async ([FromServices] IJoblyService joblyService, Guid jobId) =>
        {
            await joblyService.SetRetry(jobId);
        });

        apiGroup.MapGet("scheduled", async ([FromServices] IJoblyService joblyService, [AsParameters] BaseListRequest request) =>
        {
            var model = await joblyService.GetScheduledJobs(request);
            return model;
        });
    }
}