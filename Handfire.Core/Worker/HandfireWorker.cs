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
                .AsNoTracking()
                .Where(x => x.ProcessedTime == null)
                .OrderBy(x => x.CreateTime)
                .Take(10)
                .ToListAsync();

            foreach (var message in messages)
            {
                try
                {
                    await ProcessOutboxMessage(message);
                }
                catch (Exception)
                {
                }
            }

            // if we didn't find any messages then we wait, otherwise we query again immediately 
            if (!messages.Any())
            {
                await Task.Delay(1000);
            }
        }
    }

    private async Task ProcessOutboxMessage(OutboxMessage message)
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

        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        await context.Set<OutboxMessage>()
            .Where(x => x.Id == message.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.ProcessedTime, DateTime.UtcNow));
    }
}
