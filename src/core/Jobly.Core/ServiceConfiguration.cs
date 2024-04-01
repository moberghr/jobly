using System.Security.Policy;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Helper;
using Jobly.Core.Interceptors;
using MediatR;
using Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.SqlServer.Infrastructure.Internal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal;

namespace Jobly.Core;

public static class ServiceConfiguration
{
    private static readonly PostgresRowLockInterceptor _postgresInterceptor = new();
    private static readonly SqlServerRowLockInterceptor _sqlServerInterceptor = new();

    private static readonly SaveChangesConcurrencyTokenInterceptor _saveChangesInterceptor = new();

    public static IServiceCollection AddJobly<TContext>(this IServiceCollection services, Action<JoblyConfiguration>? options = null)
        where TContext : DbContext
    {
        return CreateJoblyServices<TContext>(services, options);
    }

    /// <summary>
    /// Register PostgreSQL Notify/Listner provider for Jobly, it is only available on PostgreSQL
    /// make sure you register the AddPostgresNotifyWakeupProvider in the worker service so that the
    /// worker will be able to listen for the notification
    /// </summary>
    /// <param name="services">CI service</param>
    /// <typeparam name="TContext">Db context</typeparam>
    /// <returns></returns>
    public static IServiceCollection AddPostgresNotifyJob<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        services.AddScoped<IJoblyNotifer>(x => new PostgresNotifyNotifyProvider<TContext>(x.GetRequiredService<TContext>()));
        return services;
    }
    
    private static IServiceCollection CreateJoblyServices<TContext>(this IServiceCollection services, Action<JoblyConfiguration>? options)
        where TContext : DbContext
    {
        var assembly = typeof(ServiceConfiguration).Assembly;

        var builder = services.AddControllersWithViews();
        builder.AddApplicationPart(assembly)
            .AddRazorRuntimeCompilation();

        if (options != null)
        {
            services.Configure(options);
        }

        services.Configure<MvcRazorRuntimeCompilationOptions>(options =>
        {
            options.FileProviders.Add(new EmbeddedFileProvider(assembly));
        });
        
        services.AddScoped<IPublisher>(x => new Publisher<TContext>(x.GetRequiredService<TContext>(),
            x.GetRequiredService<IOptions<JoblyConfiguration>>(), x));
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
        AddJobStateEntity(modelBuilder);
        AddRecurringJobEntity(modelBuilder);
        AddBatchEntity(modelBuilder);
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
        job.Property(p => p.Priority);
        job.Property(p => p.RetriedTimes);
        job.Property(p => p.MaxRetries);
        job.Property(p => p.ParentJobId);

        job.HasOne(p => p.Batch)
            .WithOne(p => p.Job);

        job.HasMany(x => x.ChildJobs)
            .WithOne(x => x.ParentJob)
            .HasForeignKey(x => x.ParentJobId);

        job.HasMany(p => p.JobStates)
            .WithOne(p => p.Job);

        job.HasOne(p => p.ParentBatch)
            .WithMany(p => p.Jobs)
            .HasForeignKey(p => p.BatchId);
        
        job.HasIndex(p => new {p.CurrentState, p.Priority, p.ScheduleTime})
            .IsDescending(false, false, false)
            .HasFilter("\"current_state\" = 1");
            
        job.HasIndex(p => p.CurrentState);
    }

    private static void AddJobStateEntity(ModelBuilder modelBuilder)
    {
        var jobState = modelBuilder.Entity<JobState>();
        jobState.ToTable(nameof(JobState));

        jobState.Property(p => p.Id);
        jobState.HasKey(p => p.Id);

        jobState.Property(p => p.State);
        jobState.Property(p => p.DateTime);
        jobState.Property(p => p.Message);

        jobState.HasOne(p => p.Job)
            .WithMany(p => p.JobStates);
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

        batch.HasOne(p => p.Job)
            .WithOne(p => p.Batch)
            .HasForeignKey<Batch>(p => p.Id);
    }
}