using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Interceptors;
using Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.SqlServer.Infrastructure.Internal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
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

    public static IServiceCollection AddJobly<TContext>(this IServiceCollection services,
        Action<JoblyConfiguration> options)
        where TContext : DbContext
    {
        services.AddOptions<JoblyConfiguration>()
            .Configure(options);
        
        return CreateJoblyServices<TContext>(services);
    }

    public static IServiceCollection AddJobly<TContext>(this IServiceCollection services,
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
        var assembly = typeof(ServiceConfiguration).Assembly;

        var builder = services.AddControllersWithViews();
        builder.AddApplicationPart(assembly)
            .AddRazorRuntimeCompilation();

        services.Configure<MvcRazorRuntimeCompilationOptions>(options =>
        {
            options.FileProviders.Add(new EmbeddedFileProvider(assembly));
        });

        services.AddScoped<IPublisher>(x => new Publisher<TContext>(x.GetRequiredService<TContext>(),
            x.GetRequiredService<IOptions<JoblyConfiguration>>()));

        services.AddScoped<IRecurringJobPublisher>(x =>
            new RecurringJobPublisher<TContext>(x.GetRequiredService<TContext>()));
        services.AddScoped<IJoblyService>(x => new JoblyService<TContext>(x.GetRequiredService<TContext>()));
        services.AddTransient<IBatchPublisher, BatchPublisher<TContext>>();

        return services;
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

        var builderExtension = optionsBuilder.Options.FindExtension<CoreOptionsExtension>();

        if (builderExtension == null)
        {
            throw new ArgumentException("No CoreOptionsExtension found");
        }

        if (builderExtension.Interceptors == null)
        {
            throw new ArgumentException("Interceptors don't contains the configuration for this database type");
        }

        optionsBuilder.AddInterceptors(_saveChangesInterceptor);

        optionsBuilder.ConfigureWarnings(w => w.Ignore(SqlServerEventId.SavepointsDisabledBecauseOfMARS));

        return optionsBuilder;
    }

    public static void AddOutboxStateEntity(this ModelBuilder modelBuilder)
    {
        AddJobEntity(modelBuilder);
        AddRecurringJobEntity(modelBuilder);
        AddBatchEntity(modelBuilder);
        AddServerEntity(modelBuilder);
        AddWorkerEntity(modelBuilder);
        AddMessageEntity(modelBuilder);
        AddJobLogEntity(modelBuilder);
        AddStatisticEntity(modelBuilder);
    }

    private static void AddJobEntity(ModelBuilder modelBuilder)
    {
        var job = modelBuilder.Entity<Job>();
        job.ToTable(nameof(Job));

        job.Property(p => p.Id);
        job.HasKey(p => p.Id);

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
        job.Property(p => p.MessageId);
        job.Property(p => p.ExpireAt);
        job.Property(p => p.LastKeepAlive);
        job.Property(p => p.TraceId);
        job.Property(p => p.SpawnedByJobId);

        job.HasIndex(p => p.ExpireAt);
        job.HasIndex(p => p.TraceId);

        job.HasOne(p => p.MessageEntity)
            .WithMany()
            .HasForeignKey(p => p.MessageId);

        // Configure one-to-many relationship with Batch
        job.HasOne(p => p.Batch)
            .WithMany(p => p.Jobs)
            .HasForeignKey(p => p.BatchId); // Job has the foreign key

        job.HasMany(x => x.ChildJobs)
            .WithOne(x => x.ParentJob)
            .HasForeignKey(x => x.ParentJobId);

        job.HasIndex(p => new {p.CurrentState, p.Queue, p.ScheduleTime})
            .IsDescending(false, false, false);

        job.HasIndex(p => p.CurrentState);
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
        recurringJob.Property(p => p.CreatedAt);
        recurringJob.Property(p => p.NextExecution);
        recurringJob.Property(p => p.LastExecution);

        recurringJob.HasMany(p => p.Jobs).WithOne(p => p.RecurringJob).HasForeignKey(p => p.RecurringJobId);
        recurringJob.HasOne(p => p.NextJob);
        recurringJob.HasOne(p => p.LastJob);

        recurringJob.Property(p => p.Version).IsConcurrencyToken();
    }

    private static void AddBatchEntity(ModelBuilder modelBuilder)
    {
        var batch = modelBuilder.Entity<Batch>();
        batch.ToTable(nameof(Batch));

        batch.Property(p => p.Id);
        batch.HasKey(p => p.Id);

        batch.Property(p => p.Counter);
        batch.Property(p => p.ContinuationOptions);

        // batch.HasOne(p => p.ParentJob);
        // .WithOne(p => p.Batch)
        // .HasForeignKey<Batch>(p => p.Id);

        // batch.HasMany(p => p.Jobs)
        //     .WithOne(p => p.Batch)
        //     .HasForeignKey(p => p.BatchId); // Job has the foreign key
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

        worker.HasOne(p => p.Server)
            .WithMany()
            .HasForeignKey(p => p.ServerId);
    }

    private static void AddMessageEntity(ModelBuilder modelBuilder)
    {
        var message = modelBuilder.Entity<Message>();
        message.ToTable(nameof(Message));

        message.Property(p => p.Id);
        message.HasKey(p => p.Id);

        message.Property(p => p.Type);
        message.Property(p => p.Payload);
        message.Property(p => p.Queue);
        message.Property(p => p.CreateTime);
        message.Property(p => p.CurrentState);
        message.Property(p => p.JobCount);
        message.Property(p => p.ExpireAt);

        message.HasIndex(p => new { p.CurrentState, p.Queue });
        message.HasIndex(p => p.ExpireAt);
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

        jobLog.HasIndex(p => p.JobId);
    }

    private static void AddStatisticEntity(ModelBuilder modelBuilder)
    {
        var stat = modelBuilder.Entity<Statistic>();
        stat.ToTable(nameof(Statistic));

        stat.Property(p => p.Key);
        stat.HasKey(p => p.Key);
        stat.Property(p => p.Value);

        stat.HasData(
            new Statistic { Key = "stats:succeeded", Value = 0 },
            new Statistic { Key = "stats:failed", Value = 0 },
            new Statistic { Key = "stats:deleted", Value = 0 }
        );
    }
}