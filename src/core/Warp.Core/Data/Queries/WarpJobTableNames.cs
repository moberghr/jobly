using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Warp.Core.Data.Entities;
using Warp.Core.Entities;

namespace Warp.Core.Data.Queries;

/// <summary>
/// Snapshot of the resolved table/column names for <see cref="Job"/> (and <see cref="Server"/>
/// for <c>ServerCleanup</c>) as seen through the user's configured EF Core naming
/// conventions (e.g. <c>UseSnakeCaseNamingConvention</c>) and schema override. Read once at
/// DI setup and interpolated into the cached SQL strings so per-fetch calls pay zero
/// string-formatting cost.
/// </summary>
public sealed class WarpJobTableNames
{
    public string? Schema { get; init; }

    public required string Table { get; init; }

    public string? ServerSchema { get; init; }

    public required string ServerTable { get; init; }

    public required string Id { get; init; }

    public required string Kind { get; init; }

    public required string Type { get; init; }

    public required string Message { get; init; }

    public required string CreateTime { get; init; }

    public required string ScheduleTime { get; init; }

    public required string CurrentState { get; init; }

    public required string RetriedTimes { get; init; }

    public required string MaxRetries { get; init; }

    public required string Queue { get; init; }

    public required string ParentJobId { get; init; }

    public required string CurrentWorkerId { get; init; }

    public required string HandlerType { get; init; }

    public required string ExpireAt { get; init; }

    public required string LastKeepAlive { get; init; }

    public required string TraceId { get; init; }

    public required string SpawnedByJobId { get; init; }

    public required string JobCount { get; init; }

    public required string ContinuationOptions { get; init; }

    public required string CancellationMode { get; init; }

    public required string Metadata { get; init; }

    public required string ParentSpanId { get; init; }

    public static WarpJobTableNames FromModel(IModel model)
    {
        var entity = model.FindEntityType(typeof(Job))
            ?? throw new InvalidOperationException("Job entity not registered in the DbContext model.");

        var serverEntity = model.FindEntityType(typeof(Server))
            ?? throw new InvalidOperationException("Server entity not registered in the DbContext model.");

        string Col(string propertyName)
        {
            var prop = entity.FindProperty(propertyName)
                ?? throw new InvalidOperationException($"Job.{propertyName} property not found in model.");

            return prop.GetColumnName()
                ?? throw new InvalidOperationException($"Job.{propertyName} has no resolved column name.");
        }

        return new WarpJobTableNames
        {
            Schema = entity.GetSchema(),
            Table = entity.GetTableName()
                ?? throw new InvalidOperationException("Job entity has no resolved table name."),
            ServerSchema = serverEntity.GetSchema(),
            ServerTable = serverEntity.GetTableName()
                ?? throw new InvalidOperationException("Server entity has no resolved table name."),
            Id = Col(nameof(Job.Id)),
            Kind = Col(nameof(Job.Kind)),
            Type = Col(nameof(Job.Type)),
            Message = Col(nameof(Job.Message)),
            CreateTime = Col(nameof(Job.CreateTime)),
            ScheduleTime = Col(nameof(Job.ScheduleTime)),
            CurrentState = Col(nameof(Job.CurrentState)),
            RetriedTimes = Col(nameof(Job.RetriedTimes)),
            MaxRetries = Col(nameof(Job.MaxRetries)),
            Queue = Col(nameof(Job.Queue)),
            ParentJobId = Col(nameof(Job.ParentJobId)),
            CurrentWorkerId = Col(nameof(Job.CurrentWorkerId)),
            HandlerType = Col(nameof(Job.HandlerType)),
            ExpireAt = Col(nameof(Job.ExpireAt)),
            LastKeepAlive = Col(nameof(Job.LastKeepAlive)),
            TraceId = Col(nameof(Job.TraceId)),
            SpawnedByJobId = Col(nameof(Job.SpawnedByJobId)),
            JobCount = Col(nameof(Job.JobCount)),
            ContinuationOptions = Col(nameof(Job.ContinuationOptions)),
            CancellationMode = Col(nameof(Job.CancellationMode)),
            Metadata = Col(nameof(Job.Metadata)),
            ParentSpanId = Col(nameof(Job.ParentSpanId)),
        };
    }
}
