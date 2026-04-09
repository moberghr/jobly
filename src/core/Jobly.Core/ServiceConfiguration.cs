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
            x.GetRequiredService<TimeProvider>()));

        services.AddScoped<IMediator>(x => new Mediator(x));

        services.AddScoped<IRecurringJobPublisher>(x =>
            new RecurringJobPublisher<TContext>(x.GetRequiredService<TContext>(), x.GetRequiredService<TimeProvider>()));
        services.AddScoped<IJobQueryService>(x => new JobQueryService<TContext>(x.GetRequiredService<TContext>(), x.GetRequiredService<TimeProvider>()));
        services.AddScoped<IJobCommandService>(x => new JobCommandService<TContext>(x.GetRequiredService<TContext>(), x.GetRequiredService<TimeProvider>(), x.GetRequiredService<IOptions<JoblyConfiguration>>()));
        services.AddScoped<IJobGroupQueryService>(x => new JobGroupQueryService<TContext>(x.GetRequiredService<TContext>()));
        services.AddScoped<IRecurringJobService>(x => new RecurringJobService<TContext>(x.GetRequiredService<TContext>(), x.GetRequiredService<TimeProvider>()));
        services.AddScoped<IDashboardStatsService>(x => new DashboardStatsService<TContext>(x.GetRequiredService<TContext>(), x.GetRequiredService<TimeProvider>()));
        services.AddScoped<IServerCommandService>(x => new ServerCommandService<TContext>(x.GetRequiredService<TContext>(), x.GetRequiredService<TimeProvider>()));
        services.AddTransient<IBatchPublisher, BatchPublisher<TContext>>();

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

    public static void AddOutboxStateEntity(this ModelBuilder modelBuilder)
    {
        AddJobEntity(modelBuilder);
        AddRecurringJobEntity(modelBuilder);
        AddRecurringJobLogEntity(modelBuilder);
        AddServerEntity(modelBuilder);
        AddWorkerEntity(modelBuilder);
        AddWorkerGroupEntity(modelBuilder);
        AddJobLogEntity(modelBuilder);
        AddStatisticEntity(modelBuilder);
        AddCounterEntity(modelBuilder);
        AddServerTaskEntity(modelBuilder);
        AddServerLogEntity(modelBuilder);
    }

    private static void AddJobEntity(ModelBuilder modelBuilder)
    {
        var job = modelBuilder.Entity<Job>();
        job.ToTable(nameof(Job));

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
        job.Property(p => p.ConcurrencyKey);
        job.HasIndex(p => p.ConcurrencyKey);
    }

    private static void AddRecurringJobEntity(ModelBuilder modelBuilder)
    {
        var recurringJob = modelBuilder.Entity<RecurringJob>();
        recurringJob.ToTable(nameof(RecurringJob));

        recurringJob.Property(p => p.Id);
        recurringJob.HasKey(p => p.Id);

        recurringJob.Property(p => p.Name);
        recurringJob.HasIndex(p => p.Name).IsUnique();

        recurringJob.Property(p => p.Cron);
        recurringJob.Property(p => p.Queue);
        recurringJob.Property(p => p.CreatedAt);
        recurringJob.Property(p => p.NextExecution);
        recurringJob.Property(p => p.LastExecution);

        recurringJob.Property(p => p.Version).IsConcurrencyToken();
    }

    private static void AddRecurringJobLogEntity(ModelBuilder modelBuilder)
    {
        var log = modelBuilder.Entity<RecurringJobLog>();
        log.ToTable(nameof(RecurringJobLog));

        log.Property(p => p.Id);
        log.HasKey(p => p.Id);

        log.Property(p => p.RecurringJobId);
        log.Property(p => p.JobId);
        log.Property(p => p.CreatedAt);

        log.HasIndex(p => p.RecurringJobId);

        log.HasIndex(p => p.JobId);
        log.HasOne(p => p.Job).WithMany().HasForeignKey(p => p.JobId).OnDelete(DeleteBehavior.SetNull);
    }

    private static void AddServerEntity(ModelBuilder modelBuilder)
    {
        var server = modelBuilder.Entity<Server>();
        server.ToTable(nameof(Server));

        server.Property(p => p.Id);
        server.HasKey(p => p.Id);

        server.Property(p => p.StartedTime);

        server.Property(p => p.LastHeartbeatTime);

        server.Property(p => p.ServiceCount);

        server.Property(p => p.PausedAt);
    }

    private static void AddWorkerEntity(ModelBuilder modelBuilder)
    {
        var worker = modelBuilder.Entity<Worker>();
        worker.ToTable(nameof(Worker));

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
    }

    private static void AddWorkerGroupEntity(ModelBuilder modelBuilder)
    {
        var wg = modelBuilder.Entity<WorkerGroup>();
        wg.ToTable(nameof(WorkerGroup));

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
    }

    private static void AddJobLogEntity(ModelBuilder modelBuilder)
    {
        var jobLog = modelBuilder.Entity<JobLog>();
        jobLog.ToTable(nameof(JobLog));

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
    }

    private static void AddStatisticEntity(ModelBuilder modelBuilder)
    {
        var stat = modelBuilder.Entity<Statistic>();
        stat.ToTable(nameof(Statistic));

        stat.Property(p => p.Key);
        stat.HasKey(p => p.Key);
        stat.Property(p => p.Value);

        // No seed data — Statistic rows are created by the counter aggregator on demand.
    }

    private static void AddCounterEntity(ModelBuilder modelBuilder)
    {
        var counter = modelBuilder.Entity<Counter>();
        counter.ToTable(nameof(Counter));

        counter.Property(p => p.Id);
        counter.HasKey(p => p.Id);
        counter.Property(p => p.Key);
        counter.Property(p => p.Value);

        counter.HasIndex(p => p.Key);
    }

    private static void AddServerTaskEntity(ModelBuilder modelBuilder)
    {
        var serverTask = modelBuilder.Entity<ServerTask>();
        serverTask.ToTable(nameof(ServerTask));

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
    }

    private static void AddServerLogEntity(ModelBuilder modelBuilder)
    {
        var serverLog = modelBuilder.Entity<ServerLog>();
        serverLog.ToTable(nameof(ServerLog));

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
    }
}
