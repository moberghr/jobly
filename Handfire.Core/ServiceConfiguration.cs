using Handfire.Core.Entities;
using Handfire.Core.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Handfire.Core;

public static class ServiceConfiguration
{
    public static void AddHandfire<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        services.AddScoped<IPublisher>(x => new Publisher<TContext>(x.GetRequiredService<TContext>()));
        services.AddHostedService<HandfireWorker<TContext>>();
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
