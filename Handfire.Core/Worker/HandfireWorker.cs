using System.Text.Json;
using Handfire.Core.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Handfire.Core.Worker;

public class HandfireWorker<TContext> : BackgroundService
    where TContext : DbContext
{
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public HandfireWorker(IServiceScopeFactory serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _serviceScopeFactory.CreateScope();

            var context = scope.ServiceProvider.GetRequiredService<TContext>();

            var messages = await context.Set<OutboxMessage>()
                .Where(x => x.ProcessedTime == null)
                .ToListAsync();

            foreach (var message in messages)
            {
                try
                {
                    await ProcessOutboxMessage(context, message);
                }
                catch (Exception)
                {
                }
            }

            await Task.Delay(100000);
        }
    }

    private async Task ProcessOutboxMessage(TContext context, OutboxMessage message)
    {
        var type = Type.GetType(message.Type);

        if (type is null)
        {
            throw new HandfireException($"Unknown type {message.Type}");
        }

        var request = JsonSerializer.Deserialize(message.Message, type);

        if (request is null)
        {
            throw new HandfireException($"Unable to deserialize message {message.Message} to type {message.Type}");
        }

        using var scope = _serviceScopeFactory.CreateScope();

        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await mediator.Send(request);

        message.ProcessedTime = DateTime.UtcNow;

        await context.SaveChangesAsync();
    }
}
