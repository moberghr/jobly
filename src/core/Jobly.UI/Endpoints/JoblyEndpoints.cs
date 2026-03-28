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

        // ==================== Dashboard ====================

        apiGroup.MapGet("status", async ([FromServices] IJoblyService joblyService) =>
        {
            return await joblyService.GetJoblyStatus();
        });

        // ==================== Jobs by State ====================

        apiGroup.MapGet("jobs/enqueued", async ([FromServices] IJoblyService joblyService, [AsParameters] BaseListRequest request) =>
        {
            return await joblyService.GetJobsList(request, State.Enqueued);
        });

        apiGroup.MapGet("jobs/completed", async ([FromServices] IJoblyService joblyService, [AsParameters] BaseListRequest request) =>
        {
            return await joblyService.GetJobsList(request, State.Completed);
        });

        apiGroup.MapGet("jobs/failed", async ([FromServices] IJoblyService joblyService, [AsParameters] BaseListRequest request) =>
        {
            return await joblyService.GetJobsList(request, State.Failed);
        });

        apiGroup.MapGet("jobs/processing", async ([FromServices] IJoblyService joblyService, [AsParameters] BaseListRequest request) =>
        {
            return await joblyService.GetJobStatesInProcess(request);
        });

        apiGroup.MapGet("jobs/scheduled", async ([FromServices] IJoblyService joblyService, [AsParameters] BaseListRequest request) =>
        {
            return await joblyService.GetScheduledJobs(request);
        });

        apiGroup.MapGet("jobs/awaiting", async ([FromServices] IJoblyService joblyService, [AsParameters] BaseListRequest request) =>
        {
            return await joblyService.GetAwaitingJobs(request);
        });

        // ==================== Job Details & Actions ====================

        apiGroup.MapGet("jobs/{jobId}", async ([FromServices] IJoblyService joblyService, Guid jobId) =>
        {
            var model = await joblyService.GetJobById(jobId);
            return model is null ? Results.NotFound() : Results.Ok(model);
        });

        apiGroup.MapPost("jobs/{jobId}/requeue", async ([FromServices] IJoblyService joblyService, Guid jobId) =>
        {
            await joblyService.RequeueJob(jobId);
        });

        apiGroup.MapPost("jobs/{jobId}/delete", async ([FromServices] IJoblyService joblyService, Guid jobId) =>
        {
            await joblyService.DeleteJob(jobId);
        });

        // ==================== Bulk Actions ====================

        apiGroup.MapPost("jobs/bulk/delete", async ([FromServices] IJoblyService joblyService, [FromBody] BulkJobRequest request) =>
        {
            return await joblyService.BulkDeleteJobs(request.JobIds);
        });

        apiGroup.MapPost("jobs/bulk/requeue", async ([FromServices] IJoblyService joblyService, [FromBody] BulkJobRequest request) =>
        {
            return await joblyService.BulkRequeueJobs(request.JobIds);
        });

        // ==================== Messages ====================

        apiGroup.MapGet("messages", async ([FromServices] IJoblyService joblyService, [AsParameters] BaseListRequest request) =>
        {
            return await joblyService.GetMessages(request);
        });

        apiGroup.MapGet("messages/{messageId}", async ([FromServices] IJoblyService joblyService, Guid messageId) =>
        {
            var model = await joblyService.GetMessageById(messageId);
            return model is null ? Results.NotFound() : Results.Ok(model);
        });

        // ==================== Recurring Jobs ====================

        apiGroup.MapGet("recurring", async ([FromServices] IJoblyService joblyService, [AsParameters] BaseListRequest request) =>
        {
            return await joblyService.GetRecurringJobs(request);
        });

        apiGroup.MapPost("recurring/{id}/trigger", async ([FromServices] IJoblyService joblyService, int id) =>
        {
            await joblyService.TriggerRecurringJob(id);
        });

        apiGroup.MapDelete("recurring/{id}", async ([FromServices] IJoblyService joblyService, int id) =>
        {
            await joblyService.DeleteRecurringJob(id);
        });

        apiGroup.MapGet("jobs/{jobId}/logs", async ([FromServices] IJoblyService joblyService, Guid jobId) =>
        {
            var job = await joblyService.GetJobById(jobId);
            return job?.Logs ?? new List<JobLogModel>();
        });

        // ==================== Statistics ====================

        apiGroup.MapGet("stats/history", async ([FromServices] IJoblyService joblyService, [FromQuery] int? hours) =>
        {
            return await joblyService.GetStatsHistory(hours ?? 24);
        });

        // ==================== Servers ====================

        apiGroup.MapGet("servers", async ([FromServices] IJoblyService joblyService) =>
        {
            return await joblyService.GetServers();
        });

        // ==================== Legacy endpoints (backwards compat) ====================

        apiGroup.MapGet("created", async ([FromServices] IJoblyService joblyService, [AsParameters] BaseListRequest request) =>
        {
            return await joblyService.GetJobsList(request, State.Enqueued);
        });

        apiGroup.MapGet("completed", async ([FromServices] IJoblyService joblyService, [AsParameters] BaseListRequest request) =>
        {
            return await joblyService.GetJobsList(request, State.Completed);
        });

        apiGroup.MapGet("failed", async ([FromServices] IJoblyService joblyService, [AsParameters] BaseListRequest request) =>
        {
            return await joblyService.GetJobsList(request, State.Failed);
        });

        apiGroup.MapGet("processing", async ([FromServices] IJoblyService joblyService, [AsParameters] BaseListRequest request) =>
        {
            return await joblyService.GetJobStatesInProcess(request);
        });

        apiGroup.MapGet("scheduled", async ([FromServices] IJoblyService joblyService, [AsParameters] BaseListRequest request) =>
        {
            return await joblyService.GetScheduledJobs(request);
        });

    }
}
