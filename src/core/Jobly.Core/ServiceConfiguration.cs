using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Handlers;
using Jobly.Core.Interceptors;
using Jobly.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.SqlServer.Infrastructure.Internal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal;

namespace Jobly.Core;

public static class ServiceConfiguration
{
    private static readonly PostgresRowLockInterceptor _postgresInterceptor = new();
    private static readonly SqlServerRowLockInterceptor _sqlServerInterceptor = new();

    private static readonly SaveChangesConcurrencyTokenInterceptor _saveChangesInterceptor = new();

    public static IServiceCollection AddJobly<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        services.AddOptions<JoblyConfiguration>();
        return CreateJoblyServices<TContext>(services);
    }

    public static IServiceCollection AddJobly<TContext>(
        this IServiceCollection services,
        Action<JoblyConfiguration> options)
        where TContext : DbContext
    {
        services.AddOptions<JoblyConfiguration>()
            .Configure(options);

        return CreateJoblyServices<TContext>(services);
    }

    public static IServiceCollection AddJobly<TContext>(
        this IServiceCollection services,
        IConfiguration namedConfigurationSection)
        where TContext : DbContext
    {
        services.AddOptions<JoblyConfiguration>()
            .Bind(namedConfigurationSection);
        return CreateJoblyServices<TContext>(services);
    }

    private static IServiceCollection CreateJoblyServices<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        ConfigureDbContextOptions<TContext>(services);

        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IPublisher>(x => new Publisher<TContext>(
            x.GetRequiredService<TContext>(),
            x.GetRequiredService<IOptions<JoblyConfiguration>>(),
            x.GetRequiredService<TimeProvider>(),
            x));

        services.AddScoped<IMediator>(x => new Mediator(x));

        services.AddScoped<IRecurringJobPublisher>(x =>
            new RecurringJobPublisher<TContext>(x.GetRequiredService<TContext>(), x.GetRequiredService<TimeProvider>()));
        services.AddScoped<IJobQueryService>(x => new JobQueryService<TContext>(x.GetRequiredService<TContext>(), x.GetRequiredService<TimeProvider>()));
        services.AddScoped<IJobCommandService>(x => new JobCommandService<TContext>(x.GetRequiredService<TContext>(), x.GetRequiredService<TimeProvider>(), x.GetRequiredService<IOptions<JoblyConfiguration>>()));
        services.AddScoped<IJobGroupQueryService>(x => new JobGroupQueryService<TContext>(x.GetRequiredService<TContext>()));
        services.AddScoped<IRecurringJobService>(x => new RecurringJobService<TContext>(x.GetRequiredService<TContext>(), x.GetRequiredService<TimeProvider>()));
        services.AddScoped<IDashboardStatsService>(x => new DashboardStatsService<TContext>(x.GetRequiredService<TContext>(), x.GetRequiredService<TimeProvider>()));
        services.AddScoped<IServerCommandService>(x => new ServerCommandService<TContext>(x.GetRequiredService<TContext>(), x.GetRequiredService<TimeProvider>()));
        services.AddScoped<IBatchPublisher>(x => new BatchPublisher<TContext>(
            x.GetRequiredService<TContext>(),
            x.GetRequiredService<IOptions<JoblyConfiguration>>(),
            x.GetRequiredService<TimeProvider>(),
            x));

        services.AddScoped<JobContext>();
        services.AddScoped<IJobContext>(x => x.GetRequiredService<JobContext>());

        return services;
    }

    private static void ConfigureDbContextOptions<TContext>(IServiceCollection services)
        where TContext : DbContext
    {
        var descriptor = services.LastOrDefault(d => d.ServiceType == typeof(DbContextOptions<TContext>));
        if (descriptor?.ImplementationFactory == null)
        {
            return;
        }

        var originalFactory = descriptor.ImplementationFactory;
        services.Remove(descriptor);
        services.Add(ServiceDescriptor.Describe(
            typeof(DbContextOptions<TContext>),
            sp =>
            {
                var options = (DbContextOptions<TContext>)originalFactory(sp);
                var builder = new DbContextOptionsBuilder<TContext>(options);
                builder.AddJoblyInterceptors();
                builder.ReplaceService<IModelCustomizer, JoblyModelCustomizer>();
                return builder.Options;
            },
            descriptor.Lifetime));
    }

    public static DbContextOptionsBuilder AddJoblyInterceptors(this DbContextOptionsBuilder optionsBuilder)
    {
        var extensions = optionsBuilder.Options.Extensions;

        foreach (var extension in extensions)
        {
            if (extension is NpgsqlOptionsExtension)
            {
                optionsBuilder.AddInterceptors(_postgresInterceptor);
                break;
            }

            if (extension is SqlServerOptionsExtension)
            {
                optionsBuilder.AddInterceptors(_sqlServerInterceptor);
                break;
            }
        }

        var builderExtension = optionsBuilder.Options.FindExtension<CoreOptionsExtension>() ?? throw new ArgumentException("No CoreOptionsExtension found", nameof(optionsBuilder));
        if (builderExtension.Interceptors == null)
        {
            throw new ArgumentException("Interceptors don't contains the configuration for this database type", nameof(optionsBuilder));
        }

        optionsBuilder.AddInterceptors(_saveChangesInterceptor);

        optionsBuilder.ConfigureWarnings(w => w.Ignore(SqlServerEventId.SavepointsDisabledBecauseOfMARS));

        return optionsBuilder;
    }

    public static void AddOutboxStateEntity(this ModelBuilder modelBuilder, string? schema = "jobly")
    {
        AddJobEntity(modelBuilder, schema);
        AddRecurringJobEntity(modelBuilder, schema);
        AddRecurringJobLogEntity(modelBuilder, schema);
        AddServerEntity(modelBuilder, schema);
        AddWorkerEntity(modelBuilder, schema);
        AddWorkerGroupEntity(modelBuilder, schema);
        AddJobLogEntity(modelBuilder, schema);
        AddStatisticEntity(modelBuilder, schema);
        AddCounterEntity(modelBuilder, schema);
        AddServerTaskEntity(modelBuilder, schema);
        AddServerLogEntity(modelBuilder, schema);
    }

    private static void AddJobEntity(ModelBuilder modelBuilder, string? schema)
    {
        var job = modelBuilder.Entity<Job>();

        job.Property(p => p.Id);
        job.HasKey(p => p.Id);

        job.Property(p => p.Kind);
        job.Property(p => p.Type);
        job.Property(p => p.Message);
        job.Property(p => p.CreateTime);
        job.Property(p => p.ScheduleTime);
        job.Property(p => p.CurrentState);
        job.Property(p => p.Queue);
        job.Property(p => p.RetriedTimes);
        job.Property(p => p.MaxRetries);
        job.Property(p => p.ParentJobId);
        job.Property(p => p.HandlerType);
        job.Property(p => p.ExpireAt);
        job.Property(p => p.LastKeepAlive);
        job.Property(p => p.TraceId);
        job.Property(p => p.SpawnedByJobId);
        job.Property(p => p.JobCount);
        job.Property(p => p.ContinuationOptions);

        job.HasMany(x => x.ChildJobs)
            .WithOne(x => x.ParentJob)
            .HasForeignKey(x => x.ParentJobId);

        // Worker fetch: Kind + State + Queue + ScheduleTime
        job.HasIndex(p => new { p.Kind, p.CurrentState, p.Queue, p.ScheduleTime });

        // Child job queries + failed children check during completion
        job.HasIndex(p => new { p.ParentJobId, p.CurrentState });

        // Message/Batch listing pages
        job.HasIndex(p => new { p.Kind, p.CurrentState, p.CreateTime });

        job.HasIndex(p => p.ExpireAt);
        job.HasIndex(p => p.TraceId);

        job.Property(p => p.CancellationMode);

        job.Property(p => p.Metadata);

        job.Metadata.SetSchema(schema);
    }

    private static void AddRecurringJobEntity(ModelBuilder modelBuilder, string? schema)
    {
        var recurringJob = modelBuilder.Entity<RecurringJob>();

        recurringJob.Property(p => p.Id);
        recurringJob.HasKey(p => p.Id);

        recurringJob.Property(p => p.Name);
        recurringJob.HasIndex(p => p.Name).IsUnique();

        recurringJob.Property(p => p.Cron);
        recurringJob.Property(p => p.Queue);
        recurringJob.Property(p => p.CreatedAt);
        recurringJob.Property(p => p.NextExecution);
        recurringJob.Property(p => p.LastExecution);

        recurringJob.Property(p => p.DisabledAt);

        recurringJob.Property(p => p.Version).IsConcurrencyToken();

        recurringJob.Metadata.SetSchema(schema);
    }

    private static void AddRecurringJobLogEntity(ModelBuilder modelBuilder, string? schema)
    {
        var log = modelBuilder.Entity<RecurringJobLog>();

        log.Property(p => p.Id);
        log.HasKey(p => p.Id);

        log.Property(p => p.RecurringJobId);
        log.Property(p => p.JobId);
        log.Property(p => p.Skipped);
        log.Property(p => p.CreatedAt);

        log.HasIndex(p => p.RecurringJobId);

        log.HasIndex(p => p.JobId);
        log.HasOne(p => p.Job).WithMany().HasForeignKey(p => p.JobId).OnDelete(DeleteBehavior.SetNull);

        log.Metadata.SetSchema(schema);
    }

    private static void AddServerEntity(ModelBuilder modelBuilder, string? schema)
    {
        var server = modelBuilder.Entity<Server>();

        server.Property(p => p.Id);
        server.HasKey(p => p.Id);

        server.Property(p => p.StartedTime);

        server.Property(p => p.LastHeartbeatTime);

        server.Property(p => p.ServiceCount);

        server.Property(p => p.PausedAt);

        server.Metadata.SetSchema(schema);
    }

    private static void AddWorkerEntity(ModelBuilder modelBuilder, string? schema)
    {
        var worker = modelBuilder.Entity<Worker>();

        worker.Property(p => p.Id);
        worker.HasKey(p => p.Id);

        worker.Property(p => p.ServerId);
        worker.Property(p => p.StartedTime);
        worker.Property(p => p.LastHeartbeatTime);
        worker.Property(p => p.WorkerGroupId);

        worker.HasOne(p => p.Server)
            .WithMany()
            .HasForeignKey(p => p.ServerId);

        worker.HasOne(p => p.WorkerGroup)
            .WithMany()
            .HasForeignKey(p => p.WorkerGroupId);

        worker.Metadata.SetSchema(schema);
    }

    private static void AddWorkerGroupEntity(ModelBuilder modelBuilder, string? schema)
    {
        var wg = modelBuilder.Entity<WorkerGroup>();

        wg.Property(p => p.Id);
        wg.HasKey(p => p.Id);

        wg.Property(p => p.ServerId);
        wg.Property(p => p.WorkerCount);
        wg.Property(p => p.Queues);
        wg.Property(p => p.PollingIntervalMs);
        wg.Property(p => p.PausedAt);

        wg.HasOne(p => p.Server)
            .WithMany()
            .HasForeignKey(p => p.ServerId);

        wg.Metadata.SetSchema(schema);
    }

    private static void AddJobLogEntity(ModelBuilder modelBuilder, string? schema)
    {
        var jobLog = modelBuilder.Entity<JobLog>();

        jobLog.Property(p => p.Id);
        jobLog.HasKey(p => p.Id);

        jobLog.Property(p => p.JobId);
        jobLog.Property(p => p.EventType);
        jobLog.Property(p => p.Timestamp);
        jobLog.Property(p => p.Level);
        jobLog.Property(p => p.Message);
        jobLog.Property(p => p.Exception);
        jobLog.Property(p => p.DurationMs);
        jobLog.Property(p => p.WorkerId);

        jobLog.HasIndex(p => p.JobId);

        jobLog.Metadata.SetSchema(schema);
    }

    private static void AddStatisticEntity(ModelBuilder modelBuilder, string? schema)
    {
        var stat = modelBuilder.Entity<Statistic>();

        stat.Property(p => p.Key);
        stat.HasKey(p => p.Key);
        stat.Property(p => p.Value);

        // No seed data — Statistic rows are created by the counter aggregator on demand.
        stat.Metadata.SetSchema(schema);
    }

    private static void AddCounterEntity(ModelBuilder modelBuilder, string? schema)
    {
        var counter = modelBuilder.Entity<Counter>();

        counter.Property(p => p.Id);
        counter.HasKey(p => p.Id);
        counter.Property(p => p.Key);
        counter.Property(p => p.Value);

        counter.HasIndex(p => p.Key);

        counter.Metadata.SetSchema(schema);
    }

    private static void AddServerTaskEntity(ModelBuilder modelBuilder, string? schema)
    {
        var serverTask = modelBuilder.Entity<ServerTask>();

        serverTask.Property(p => p.Id);
        serverTask.HasKey(p => p.Id);
        serverTask.Property(p => p.ServerId);
        serverTask.Property(p => p.TaskName);
        serverTask.Property(p => p.IntervalSeconds);
        serverTask.Property(p => p.LastStatus);
        serverTask.Property(p => p.LastMessage);
        serverTask.Property(p => p.LastRun);
        serverTask.Property(p => p.LastDurationMs);

        serverTask.HasOne<Server>()
            .WithMany()
            .HasForeignKey(p => p.ServerId)
            .OnDelete(DeleteBehavior.Cascade);

        serverTask.HasIndex(p => p.ServerId);

        serverTask.Metadata.SetSchema(schema);
    }

    private static void AddServerLogEntity(ModelBuilder modelBuilder, string? schema)
    {
        var serverLog = modelBuilder.Entity<ServerLog>();

        serverLog.Property(p => p.Id);
        serverLog.HasKey(p => p.Id);
        serverLog.Property(p => p.ServerId);
        serverLog.Property(p => p.ServerTaskId);
        serverLog.Property(p => p.Status);
        serverLog.Property(p => p.Message);
        serverLog.Property(p => p.Timestamp);
        serverLog.Property(p => p.DurationMs);

        serverLog.HasOne<Server>()
            .WithMany()
            .HasForeignKey(p => p.ServerId)
            .OnDelete(DeleteBehavior.Cascade);

        serverLog.HasIndex(p => p.ServerId);
        serverLog.HasIndex(p => p.ServerTaskId);
        serverLog.HasIndex(p => p.Timestamp);

        serverLog.Metadata.SetSchema(schema);
    }
}
