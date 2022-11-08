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

    public async Task Publish(object message)
    {
        var outboxMessage = new OutboxMessage
        {
            CreateTime = DateTime.UtcNow,
            Message = JsonSerializer.Serialize(message),
            Type = message.GetType().AssemblyQualifiedName!
        };

        _handfireContext.OutboxMessages.Add(outboxMessage);
    }
}

public interface IPublisher<TContext>
{
    Task Publish(object message);
}
