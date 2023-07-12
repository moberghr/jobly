using Handfire.Core.Data.Entities;
using Handfire.Core.Entities;
using Handfire.Core.Interceptors;
using Handfire.Core.Worker;
using Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.SqlServer.Infrastructure.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal;

namespace Handfire.Core;

public static class ServiceConfiguration
{
    private static readonly PostgresRowLockInterceptor _postgresInterceptor = new();
    private static readonly SqlServerRowLockInterceptor _sqlServerInterceptor = new();

    private static readonly SaveChangesConcurrencyTokenInterceptor _saveChangesInterceptor = new();

    public static IServiceCollection AddHandfire<TContext>(this IServiceCollection services, int workerCount, int retries)
        where TContext : DbContext
    {
        return CreateHandfireServices<TContext>(services, workerCount, retries);
    }

    public static IServiceCollection AddHandfire<TContext>(this IServiceCollection services, int workerCount)
        where TContext : DbContext
    {
        return CreateHandfireServices<TContext>(services, workerCount, retries: 0);
    }

    private static IServiceCollection CreateHandfireServices<TContext>(this IServiceCollection services, int workerCount, int retries) where TContext : DbContext
    {
        var assembly = typeof(ServiceConfiguration).Assembly;

        var builder = services.AddControllersWithViews();
        builder.AddApplicationPart(assembly)
            .AddRazorRuntimeCompilation();

        services.Configure<MvcRazorRuntimeCompilationOptions>(options =>
        {
            options.FileProviders.Add(new EmbeddedFileProvider(assembly));
        });

        services.AddScoped<IPublisher>(x => new Publisher<TContext>(x.GetRequiredService<TContext>(), retries));
        services.AddScoped<IRecurringJobPublisher>(x => new RecurringJobPublisher<TContext>(x.GetRequiredService<TContext>()));
        services.AddScoped<IHandfireService>(x => new HandfireService<TContext>(x.GetRequiredService<TContext>()));
        services.AddTransient<IHandfireWorkerService, HandfireWorkerService<TContext>>();
        services.AddTransient<IBatchPublisher, BatchPublisher<TContext>>();

        for (var i = 0; i < workerCount; i++)
        {
            services.AddSingleton<IHostedService, HandfireWorker<TContext>>();
        }

        return services;
    }

    public static DbContextOptionsBuilder AddHandfireInterceptors(this DbContextOptionsBuilder optionsBuilder)
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
        AddBatchContinuationEntity(modelBuilder);
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
        job.Property(p => p.RetriedTimes);
        job.Property(p => p.MaxRetries);
        job.Property(p => p.ParentJobId);

        job.HasMany(x => x.ChildJobs)
            .WithOne(x => x.ParentJob)
            .HasForeignKey(x => x.ParentJobId);

        job.HasMany(p => p.JobStates)
            .WithOne(p => p.Job);

        job.HasMany(p => p.Batches)
            .WithMany(p => p.Jobs);

        job.HasMany(p => p.BatchContinuations)
            .WithOne(p => p.Job)
            .HasForeignKey(p => p.JobId);
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

        batch.Property(p => p.BatchStatus);

        batch.Property(p => p.Version).IsConcurrencyToken();

        batch.HasMany(p => p.BatchContinuations)
            .WithOne(p => p.Batch)
            .HasForeignKey(p => p.BatchId);

        batch.HasMany(p => p.Jobs)
            .WithMany(p => p.Batches);
    }

    private static void AddBatchContinuationEntity(ModelBuilder modelBuilder)
    {
        var batchContinuation = modelBuilder.Entity<BatchContinuation>();
        batchContinuation.ToTable(nameof(BatchContinuation));

        batchContinuation.Property(p => p.Id);
        batchContinuation.HasKey(p => p.Id);

        batchContinuation.HasOne(p => p.Job)
            .WithMany(p => p.BatchContinuations)
            .HasForeignKey(p => p.JobId);

        batchContinuation.HasOne(p => p.Batch)
            .WithMany(p => p.BatchContinuations)
            .HasForeignKey(p => p.BatchId);
    }
}
