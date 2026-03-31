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

        apiGroup.MapGet("status", async ([FromServices] IDashboardStatsService statsService) => await statsService.GetJoblyStatus());

        apiGroup.MapGet("jobs/enqueued", async ([FromServices] IJobQueryService jobQueryService, [AsParameters] BaseListRequest request) => await jobQueryService.GetJobsList(request, State.Enqueued));

        apiGroup.MapGet("jobs/completed", async ([FromServices] IJobQueryService jobQueryService, [AsParameters] BaseListRequest request) => await jobQueryService.GetJobsList(request, State.Completed));

        apiGroup.MapGet("jobs/failed", async ([FromServices] IJobQueryService jobQueryService, [AsParameters] BaseListRequest request) => await jobQueryService.GetJobsList(request, State.Failed));

        apiGroup.MapGet("jobs/processing", async ([FromServices] IJobQueryService jobQueryService, [AsParameters] BaseListRequest request) => await jobQueryService.GetJobStatesInProcess(request));

        apiGroup.MapGet("jobs/scheduled", async ([FromServices] IJobQueryService jobQueryService, [AsParameters] BaseListRequest request) => await jobQueryService.GetScheduledJobs(request));

        apiGroup.MapGet("jobs/awaiting", async ([FromServices] IJobQueryService jobQueryService, [AsParameters] BaseListRequest request) => await jobQueryService.GetAwaitingJobs(request));

        apiGroup.MapGet("jobs/deleted", async ([FromServices] IJobQueryService jobQueryService, [AsParameters] BaseListRequest request) => await jobQueryService.GetJobsList(request, State.Deleted));

        apiGroup.MapGet("jobs/{jobId}", async ([FromServices] IJobQueryService jobQueryService, Guid jobId) =>
        {
            var model = await jobQueryService.GetJobById(jobId);
            return model is null ? Results.NotFound() : Results.Ok(model);
        });

        apiGroup.MapGet("jobs/{jobId}/siblings", async ([FromServices] IJobQueryService jobQueryService, Guid jobId, [AsParameters] BaseListRequest request) => await jobQueryService.GetSiblingJobs(jobId, request));

        apiGroup.MapGet("jobs/{jobId}/children", async ([FromServices] IJobQueryService jobQueryService, Guid jobId, [AsParameters] BaseListRequest request) => await jobQueryService.GetChildJobs(jobId, request));

        apiGroup.MapGet("jobs/{jobId}/trace", async ([FromServices] IJobQueryService jobQueryService, Guid jobId, [AsParameters] BaseListRequest request) => await jobQueryService.GetTraceJobs(jobId, request));

        apiGroup.MapPost("jobs/{jobId}/requeue", async ([FromServices] IJobCommandService jobCommandService, Guid jobId) => await jobCommandService.RequeueJob(jobId));

        apiGroup.MapPost("jobs/{jobId}/delete", async ([FromServices] IJobCommandService jobCommandService, Guid jobId) => await jobCommandService.DeleteJob(jobId));

        apiGroup.MapPost("jobs/bulk/delete", async ([FromServices] IJobCommandService jobCommandService, [FromBody] BulkJobRequest request) => await jobCommandService.BulkDeleteJobs(request.JobIds));

        apiGroup.MapPost("jobs/bulk/requeue", async ([FromServices] IJobCommandService jobCommandService, [FromBody] BulkJobRequest request) => await jobCommandService.BulkRequeueJobs(request.JobIds));

        apiGroup.MapGet("messages", async ([FromServices] IMessageQueryService messageQueryService, [AsParameters] BaseListRequest request) => await messageQueryService.GetMessages(request));

        apiGroup.MapGet("messages/{messageId}", async ([FromServices] IMessageQueryService messageQueryService, Guid messageId) =>
        {
            var model = await messageQueryService.GetMessageById(messageId);
            return model is null ? Results.NotFound() : Results.Ok(model);
        });

        apiGroup.MapGet("messages/{messageId}/jobs", async ([FromServices] IMessageQueryService messageQueryService, Guid messageId, [AsParameters] BaseListRequest request) => await messageQueryService.GetMessageJobs(messageId, request));

        apiGroup.MapGet("recurring", async ([FromServices] IRecurringJobService recurringJobService, [AsParameters] BaseListRequest request) => await recurringJobService.GetRecurringJobs(request));

        apiGroup.MapGet("recurring/{id}", async ([FromServices] IRecurringJobService recurringJobService, int id) =>
        {
            var model = await recurringJobService.GetRecurringJobById(id);
            return model is null ? Results.NotFound() : Results.Ok(model);
        });

        apiGroup.MapGet("recurring/{id}/jobs", async ([FromServices] IRecurringJobService recurringJobService, int id, [AsParameters] BaseListRequest request) => await recurringJobService.GetRecurringJobHistory(id, request));

        apiGroup.MapPost("recurring/{id}/trigger", async ([FromServices] IRecurringJobService recurringJobService, int id) => await recurringJobService.TriggerRecurringJob(id));

        apiGroup.MapDelete("recurring/{id}", async ([FromServices] IRecurringJobService recurringJobService, int id) => await recurringJobService.DeleteRecurringJob(id));

        apiGroup.MapGet("jobs/{jobId}/logs", async ([FromServices] IJobQueryService jobQueryService, Guid jobId) =>
        {
            var job = await jobQueryService.GetJobById(jobId);
            return job?.Logs ?? [];
        });

        apiGroup.MapGet("batches", async ([FromServices] IBatchQueryService batchQueryService, [AsParameters] BaseListRequest request) => await batchQueryService.GetBatches(request));

        apiGroup.MapGet("batches/{batchId}", async ([FromServices] IBatchQueryService batchQueryService, Guid batchId) =>
        {
            var model = await batchQueryService.GetBatchById(batchId);
            return model is null ? Results.NotFound() : Results.Ok(model);
        });

        apiGroup.MapGet("batches/{batchId}/jobs", async ([FromServices] IBatchQueryService batchQueryService, Guid batchId, [AsParameters] BaseListRequest request) => await batchQueryService.GetBatchJobs(batchId, request));

        apiGroup.MapGet("stats/history", async ([FromServices] IDashboardStatsService statsService, [FromQuery] int? hours) => await statsService.GetStatsHistory(hours ?? 24));

        apiGroup.MapGet("servers", async ([FromServices] IDashboardStatsService statsService) => await statsService.GetServers());

        apiGroup.MapGet("servers/{serverId}", async ([FromServices] IDashboardStatsService statsService, Guid serverId) =>
        {
            var model = await statsService.GetServerById(serverId);
            return model is null ? Results.NotFound() : Results.Ok(model);
        });

        apiGroup.MapGet("servers/{serverId}/tasks", async ([FromServices] IDashboardStatsService statsService, Guid serverId) => await statsService.GetServerTaskSummaries(serverId));

        apiGroup.MapGet("servers/{serverId}/logs", async ([FromServices] IDashboardStatsService statsService, Guid serverId, [AsParameters] BaseListRequest request, [FromQuery] string? taskName) => await statsService.GetServerLogs(serverId, request, taskName));

        apiGroup.MapGet("created", async ([FromServices] IJobQueryService jobQueryService, [AsParameters] BaseListRequest request) => await jobQueryService.GetJobsList(request, State.Enqueued));

        apiGroup.MapGet("completed", async ([FromServices] IJobQueryService jobQueryService, [AsParameters] BaseListRequest request) => await jobQueryService.GetJobsList(request, State.Completed));

        apiGroup.MapGet("failed", async ([FromServices] IJobQueryService jobQueryService, [AsParameters] BaseListRequest request) => await jobQueryService.GetJobsList(request, State.Failed));

        apiGroup.MapGet("processing", async ([FromServices] IJobQueryService jobQueryService, [AsParameters] BaseListRequest request) => await jobQueryService.GetJobStatesInProcess(request));

        apiGroup.MapGet("scheduled", async ([FromServices] IJobQueryService jobQueryService, [AsParameters] BaseListRequest request) => await jobQueryService.GetScheduledJobs(request));
    }
}
