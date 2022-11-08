using System.Text.Json;
using Handfire.Core.Entities;

namespace Handfire.Core;

public class Publisher<TContext> : IPublisher<TContext>
    where TContext : HandfireContext
{
    private readonly TContext _handfireContext;

    public Publisher(TContext handfireContext)
    {
        _handfireContext = handfireContext;
    }

    public async Task Publish<T>(T message)
        where T : class
    {
        var outboxMessage = new OutboxMessage
        {
            CreateTime = DateTime.UtcNow,
            Message = JsonSerializer.Serialize(message),
            Type = message.GetType().AssemblyQualifiedName!
        };

        await _handfireContext.Set<OutboxMessage>().AddAsync(outboxMessage);
    }
}

public interface IPublisher<TContext>
{
    Task Publish<T>(T message)
          where T : class;
}
