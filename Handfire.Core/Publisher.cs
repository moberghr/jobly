using System.Text.Json;
using Handfire.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Handfire.Core;

public class Publisher<TContext> : IPublisher
    where TContext : DbContext
{
    private readonly TContext _context;

    public Publisher(TContext context)
    {
        _context = context;
    }

    public async Task Publish<T>(T message)
        where T : class
    {
        await CreateOutboxMessage<T>(message, scheduleTime: null);
    }

    public async Task Publish<T>(T message, DateTime scheduleTime)
        where T : class
    {
        await CreateOutboxMessage<T>(message, scheduleTime);
    }

    private async Task CreateOutboxMessage<T>(T message, DateTime? scheduleTime)
        where T : class
    {
        var outboxMessage = new OutboxMessage
        {
            CreateTime = DateTime.UtcNow,
            Message = JsonSerializer.Serialize(message),
            Type = message.GetType().AssemblyQualifiedName!,
            ScheduleTime = scheduleTime
        };

        await _context.Set<OutboxMessage>().AddAsync(outboxMessage);
    }
}

public interface IPublisher
{
    Task Publish<T>(T message) where T : class;
    Task Publish<T>(T message, DateTime scheduleTime) where T : class;
}