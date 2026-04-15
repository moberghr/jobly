using Jobly.Core;
using Jobly.Core.Enums;
using Jobly.Core.Models;
using Jobly.Core.Services;
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

        if (options.Authorization != null)
        {
            var filter = options.Authorization;
            apiGroup.AddEndpointFilter(async (context, next) =>
            {
                if (!filter.Authorize(context.HttpContext))
                {
                    return Results.Unauthorized();
                }

                return await next(context);
            });
        }

        apiGroup.MapGet("status", async ([FromServices] IDashboardStatsService statsService) => await statsService.GetJoblyStatus());

        apiGroup.MapGet("jobs/enqueued", async ([FromServices] IJobQueryService jobQueryService, [AsParameters] BaseListRequest request) => await jobQueryService.GetJobsList(request, State.Enqueued));

        apiGroup.MapGet("jobs/completed", async ([FromServices] IJobQueryService jobQueryService, [AsParameters] BaseListRequest request) => await jobQueryService.GetJobsList(request, State.Completed));

        apiGroup.MapGet("jobs/failed", async ([FromServices] IJobQueryService jobQueryService, [AsParameters] BaseListRequest request) => await jobQueryService.GetJobsList(request, State.Failed));

        apiGroup.MapGet("jobs/failed/types", async ([FromServices] IJobQueryService jobQueryService) => await jobQueryService.GetFailedJobTypeCounts());

        apiGroup.MapGet("jobs/failed/by-type", async ([FromServices] IJobQueryService jobQueryService, [AsParameters] BaseListRequest request, [FromQuery] string type) => await jobQueryService.GetFailedJobsByType(request, type));

        apiGroup.MapPost("jobs/failed/delete-by-type", async ([FromServices] IJobCommandService jobCommandService, [FromQuery] string type) => await jobCommandService.DeleteFailedJobsByType(type));

        apiGroup.MapPost("jobs/failed/requeue-by-type", async ([FromServices] IJobCommandService jobCommandService, [FromQuery] string type) => await jobCommandService.RequeueFailedJobsByType(type));

        apiGroup.MapGet("jobs/processing", async ([FromServices] IJobQueryService jobQueryService, [AsParameters] BaseListRequest request) => await jobQueryService.GetJobStatesInProcess(request));

        apiGroup.MapGet("jobs/scheduled", async ([FromServices] IJobQueryService jobQueryService, [AsParameters] BaseListRequest request) => await jobQueryService.GetScheduledJobs(request));

        apiGroup.MapGet("jobs/awaiting", async ([FromServices] IJobQueryService jobQueryService, [AsParameters] BaseListRequest request) => await jobQueryService.GetAwaitingJobs(request));

        apiGroup.MapGet("jobs/deleted", async ([FromServices] IJobQueryService jobQueryService, [AsParameters] BaseListRequest request) => await jobQueryService.GetJobsList(request, State.Deleted));

        apiGroup.MapGet("jobs/{jobId}/siblings", async ([FromServices] IJobQueryService jobQueryService, Guid jobId, [AsParameters] BaseListRequest request) => await jobQueryService.GetSiblingJobs(jobId, request));

        apiGroup.MapGet("jobs/{jobId}/children", async ([FromServices] IJobQueryService jobQueryService, Guid jobId, [AsParameters] BaseListRequest request) => await jobQueryService.GetChildJobs(jobId, request));

        apiGroup.MapGet("jobs/{jobId}/trace", async ([FromServices] IJobQueryService jobQueryService, Guid jobId, [AsParameters] BaseListRequest request) => await jobQueryService.GetTraceJobs(jobId, request));

        apiGroup.MapGet("trace/{traceId}", async ([FromServices] IJobQueryService jobQueryService, Guid traceId) => await jobQueryService.GetTraceTree(traceId));

        apiGroup.MapGet("detail/{id}", async ([FromServices] IJobQueryService jobQueryService, Guid id) =>
        {
            var result = await jobQueryService.GetJobDetailById(id);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        apiGroup.MapPost("jobs/{jobId}/requeue", async ([FromServices] IJobCommandService jobCommandService, Guid jobId) => await jobCommandService.RequeueJob(jobId));

        apiGroup.MapPost("jobs/{jobId}/delete", async ([FromServices] IJobCommandService jobCommandService, Guid jobId) => await jobCommandService.DeleteJob(jobId));

        apiGroup.MapPost("jobs/bulk/delete", async ([FromServices] IJobCommandService jobCommandService, [FromBody] BulkJobRequest request) => await jobCommandService.BulkDeleteJobs(request.JobIds));

        apiGroup.MapPost("jobs/bulk/requeue", async ([FromServices] IJobCommandService jobCommandService, [FromBody] BulkJobRequest request) => await jobCommandService.BulkRequeueJobs(request.JobIds));

        apiGroup.MapGet("messages", async ([FromServices] IJobGroupQueryService svc, [AsParameters] BaseListRequest request, string? state) => await svc.GetJobGroups(JobKind.Message, request, state));

        apiGroup.MapGet("messages/{messageId}", async ([FromServices] IJobGroupQueryService svc, Guid messageId) =>
        {
            var model = await svc.GetJobGroupById(messageId);
            return model is null ? Results.NotFound() : Results.Ok(model);
        });

        apiGroup.MapGet("messages/{messageId}/jobs", async ([FromServices] IJobGroupQueryService svc, Guid messageId, [AsParameters] BaseListRequest request, string? state) => await svc.GetJobGroupJobs(messageId, request, state));

        apiGroup.MapGet("messages/{messageId}/jobs/counts", async ([FromServices] IJobGroupQueryService svc, Guid messageId) => await svc.GetJobGroupJobCounts(messageId));

        apiGroup.MapGet("recurring", async ([FromServices] IRecurringJobService recurringJobService, [AsParameters] BaseListRequest request) => await recurringJobService.GetRecurringJobs(request));

        apiGroup.MapGet("recurring/{id}", async ([FromServices] IRecurringJobService recurringJobService, int id) =>
        {
            var model = await recurringJobService.GetRecurringJobById(id);
            return model is null ? Results.NotFound() : Results.Ok(model);
        });

        apiGroup.MapGet("recurring/{id}/jobs", async ([FromServices] IRecurringJobService recurringJobService, int id, [AsParameters] BaseListRequest request) => await recurringJobService.GetRecurringJobHistory(id, request));

        apiGroup.MapPost("recurring/{id}/trigger", async ([FromServices] IRecurringJobService recurringJobService, int id) => await recurringJobService.TriggerRecurringJob(id));

        apiGroup.MapPost("recurring/{id}/enable", async ([FromServices] IRecurringJobService recurringJobService, int id) => await recurringJobService.EnableRecurringJob(id));

        apiGroup.MapPost("recurring/{id}/disable", async ([FromServices] IRecurringJobService recurringJobService, int id) => await recurringJobService.DisableRecurringJob(id));

        apiGroup.MapDelete("recurring/{id}", async ([FromServices] IRecurringJobService recurringJobService, int id) => await recurringJobService.DeleteRecurringJob(id));

        apiGroup.MapGet("batches", async ([FromServices] IJobGroupQueryService svc, [AsParameters] BaseListRequest request, string? state) => await svc.GetJobGroups(JobKind.Batch, request, state));

        apiGroup.MapGet("batches/{batchId}", async ([FromServices] IJobGroupQueryService svc, Guid batchId) =>
        {
            var model = await svc.GetJobGroupById(batchId);
            return model is null ? Results.NotFound() : Results.Ok(model);
        });

        apiGroup.MapGet("batches/{batchId}/jobs", async ([FromServices] IJobGroupQueryService svc, Guid batchId, [AsParameters] BaseListRequest request, string? state) => await svc.GetJobGroupJobs(batchId, request, state));

        apiGroup.MapGet("batches/{batchId}/jobs/counts", async ([FromServices] IJobGroupQueryService svc, Guid batchId) => await svc.GetJobGroupJobCounts(batchId));

        apiGroup.MapGet("stats/history", async ([FromServices] IDashboardStatsService statsService, [FromQuery] int? hours) => await statsService.GetStatsHistory(hours ?? 24));

        apiGroup.MapGet("servers", async ([FromServices] IDashboardStatsService statsService) => await statsService.GetServers());

        apiGroup.MapGet("servers/{serverId}", async ([FromServices] IDashboardStatsService statsService, Guid serverId) =>
        {
            var model = await statsService.GetServerById(serverId);
            return model is null ? Results.NotFound() : Results.Ok(model);
        });

        apiGroup.MapGet("servers/{serverId}/tasks", async ([FromServices] IDashboardStatsService statsService, Guid serverId) => await statsService.GetServerTaskSummaries(serverId));

        apiGroup.MapGet("servers/{serverId}/logs", async ([FromServices] IDashboardStatsService statsService, Guid serverId, [AsParameters] BaseListRequest request, [FromQuery] string? taskName) => await statsService.GetServerLogs(serverId, request, taskName));

        apiGroup.MapPost("servers/{serverId}/pause", async ([FromServices] IServerCommandService svc, Guid serverId) =>
        {
            var result = await svc.PauseServer(serverId);
            return result ? Results.Ok() : Results.NotFound();
        });

        apiGroup.MapPost("servers/{serverId}/resume", async ([FromServices] IServerCommandService svc, Guid serverId) =>
        {
            var result = await svc.ResumeServer(serverId);
            return result ? Results.Ok() : Results.NotFound();
        });

        apiGroup.MapPost("groups/{groupId}/pause", async ([FromServices] IServerCommandService svc, Guid groupId) =>
        {
            var result = await svc.PauseWorkerGroup(groupId);
            return result ? Results.Ok() : Results.NotFound();
        });

        apiGroup.MapPost("groups/{groupId}/resume", async ([FromServices] IServerCommandService svc, Guid groupId) =>
        {
            var result = await svc.ResumeWorkerGroup(groupId);
            return result ? Results.Ok() : Results.NotFound();
        });

        apiGroup.MapGet("workers/{workerId}", async ([FromServices] IDashboardStatsService statsService, Guid workerId) =>
        {
            var model = await statsService.GetWorkerById(workerId);
            return model is null ? Results.NotFound() : Results.Ok(model);
        });

        apiGroup.MapGet("workers/{workerId}/logs", async ([FromServices] IDashboardStatsService statsService, Guid workerId, [AsParameters] BaseListRequest request) => await statsService.GetWorkerJobLogs(workerId, request));

        apiGroup.MapGet("created", async ([FromServices] IJobQueryService jobQueryService, [AsParameters] BaseListRequest request) => await jobQueryService.GetJobsList(request, State.Enqueued));

        apiGroup.MapGet("completed", async ([FromServices] IJobQueryService jobQueryService, [AsParameters] BaseListRequest request) => await jobQueryService.GetJobsList(request, State.Completed));

        apiGroup.MapGet("failed", async ([FromServices] IJobQueryService jobQueryService, [AsParameters] BaseListRequest request) => await jobQueryService.GetJobsList(request, State.Failed));

        apiGroup.MapGet("processing", async ([FromServices] IJobQueryService jobQueryService, [AsParameters] BaseListRequest request) => await jobQueryService.GetJobStatesInProcess(request));

        apiGroup.MapGet("scheduled", async ([FromServices] IJobQueryService jobQueryService, [AsParameters] BaseListRequest request) => await jobQueryService.GetScheduledJobs(request));
    }
}
