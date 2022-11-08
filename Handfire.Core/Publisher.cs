using System.Text.Json;
using Handfire.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Handfire.Core;

public class Publisher : IPublisher
{
    private readonly ISendContext _context;

    public Publisher(ISendContext context)
    {
        _context = context;
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

        await _context.Send(outboxMessage);
    }
}

public interface IPublisher
{
    Task Publish<T>(T message)
          where T : class;
}

public interface IScopedContextProvider<TContext>
      where TContext : DbContext
{
    public ISendContext Context { get; set; }
}

public class EfContextProvider<TContext> : IScopedContextProvider<TContext>
    where TContext : DbContext
{
    public EfContextProvider(TContext context)
    {
        Context = new EfScopedContext<TContext>(context);
    }

    public ISendContext Context { get; set; }
}

public class EfScopedContext<TContext> : ISendContext
    where TContext : DbContext
{
    private readonly TContext _context;

    public EfScopedContext(TContext context)
    {
        _context = context;
    }

    public async Task Send(OutboxMessage message)
    {
        await _context.Set<OutboxMessage>().AddAsync(message);
    }
}

public interface ISendContext
{

    Task Send(OutboxMessage message);
}