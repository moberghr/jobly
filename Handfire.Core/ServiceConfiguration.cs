using Handfire.Core.Data.Entities;
using Handfire.Core.Entities;
using Handfire.Core.Interceptors;
using Handfire.Core.Worker;
using Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Handfire.Core;

public static class ServiceConfiguration
{
    private static readonly ForUpdateSkipLockedCommandInterceptor _interceptor = new();
    public static void AddHandfire<TContext>(this IServiceCollection services, int workerCount)
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

        services.AddScoped<IPublisher>(x => new Publisher<TContext>(x.GetRequiredService<TContext>()));
        services.AddScoped<IRecurringJobPublisher>(x => new RecurringJobPublisher<TContext>(x.GetRequiredService<TContext>()));
        services.AddScoped<IHandfireService>(x => new HandfireService<TContext>(x.GetRequiredService<TContext>()));

        for (var i = 0; i < workerCount; i++)
        {
            services.AddSingleton<IHostedService, HandfireWorker<TContext>>();
        }
    }

    public static void AddHandfireInterceptors(this DbContextOptionsBuilder optionsBuilder) => optionsBuilder.AddInterceptors(_interceptor);

    public static void AddOutboxStateEntity(this ModelBuilder modelBuilder)
    {
        AddJobEntity(modelBuilder);
        AddJobStateEntity(modelBuilder);
        AddRecurringJobEntity(modelBuilder);
    }

    private static void AddJobEntity(ModelBuilder modelBuilder)
    {
        var job = modelBuilder.Entity<Job>();

        job.Property(p => p.Id);
        job.HasKey(p => p.Id);

        job.Property(p => p.Type);
        job.Property(p => p.Message);
        job.Property(p => p.CreateTime);
        job.Property(p => p.ScheduleTime);
        job.Property(p => p.ProcessedTime);
        job.Property(p => p.CurrentState);

        job.HasMany(p => p.JobStates)
            .WithOne(p => p.Job);
    }

    private static void AddJobStateEntity(ModelBuilder modelBuilder)
    {
        var jobState = modelBuilder.Entity<JobState>();

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

        recurringJob.Property(p => p.Version).IsRowVersion();
    }
}
