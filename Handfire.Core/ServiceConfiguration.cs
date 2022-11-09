using Handfire.Core.Entities;
using Handfire.Core.Worker;
using Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Handfire.Core;

public static class ServiceConfiguration
{
    public static void AddHandfire<TContext>(this IServiceCollection services, int workerCount)
        where TContext : DbContext
    {
        var assembly = typeof(ServiceConfiguration).Assembly;

        var builder = services.AddMvc();
        builder.AddApplicationPart(assembly)
            .AddRazorRuntimeCompilation();

        services.Configure<MvcRazorRuntimeCompilationOptions>(options =>
        {
            options.FileProviders.Add(new EmbeddedFileProvider(assembly));
        });

        services.AddScoped<IPublisher>(x => new Publisher<TContext>(x.GetRequiredService<TContext>()));
        services.AddScoped<IHandfireService>(x => new HandfireService<TContext>(x.GetRequiredService<TContext>()));

        for (var i = 0; i < workerCount; i++)
        {
            services.AddSingleton<IHostedService, HandfireWorker<TContext>>();
        }
    }

    public static void AddOutboxStateEntity(this ModelBuilder modelBuilder)
    {
        var outbox = modelBuilder.Entity<OutboxMessage>();

        outbox.Property(p => p.Id);
        outbox.HasKey(p => p.Id);

        outbox.Property(p => p.Type);
        outbox.Property(p => p.Message);
        outbox.Property(p => p.CreateTime);
        outbox.Property(p => p.ProcessedTime);
    }
}
