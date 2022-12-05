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

    public async Task Publish<T>(T message, DateTime? scheduleTime = null)
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
    Task Publish<T>(T message, DateTime? scheduleTime = null)
          where T : class;
}