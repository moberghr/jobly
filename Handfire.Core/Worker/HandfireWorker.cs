using System.Text.Json;
using Handfire.Core.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Handfire.Core.Worker;

public class HandfireWorker<TContext> : BackgroundService
    where TContext : DbContext
{
    public static int Counter = 0;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<HandfireWorker<TContext>> _logger;
    private readonly string _workerId = Guid.NewGuid().ToString();

    public HandfireWorker(IServiceScopeFactory serviceScopeFactory, ILogger<HandfireWorker<TContext>> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _serviceScopeFactory.CreateScope();

            var context = scope.ServiceProvider.GetRequiredService<TContext>();

            using var transaction = await context.Database.BeginTransactionAsync();

            var message = await context.Set<OutboxMessage>()
                .FromSqlRaw("SELECT * from outbox_message WHERE processed_time is null LIMIT 1 FOR UPDATE SKIP LOCKED ")
                .AsNoTracking()
                .FirstOrDefaultAsync();

            // if we didn't find any messages then we wait, otherwise we query again immediately 
            if (message == null)
            {
                await Task.Delay(1000);

                continue;
            }

            Interlocked.Increment(ref Counter);

            _logger.LogInformation("Worker {workerId} fetched message {id}", _workerId, message.Id);

            try
            {
                await ProcessOutboxMessage(message);
            }
            catch (Exception)
            {

                throw;
            }

            await context.Set<OutboxMessage>()
               .Where(x => x.Id == message.Id)
               .ExecuteUpdateAsync(x => x.SetProperty(y => y.ProcessedTime, DateTime.UtcNow));

            transaction.Commit();
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
    }
}
