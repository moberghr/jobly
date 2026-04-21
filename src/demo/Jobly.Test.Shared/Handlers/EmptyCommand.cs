using Jobly.Core.Handlers;

namespace Jobly.Core.Handlers;

public class EmptyCommand : IJobHandler<EmptyRequest>
{
    public Task HandleAsync(EmptyRequest message, CancellationToken cancellationToken) => Task.CompletedTask;
}

public class EmptyRequest : IJob;

public class EmptyMessage : IMessage;

public class EmptyMessageHandler1 : IMessageHandler<EmptyMessage>
{
    public Task HandleAsync(EmptyMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
}

public class EmptyMessageHandler2 : IMessageHandler<EmptyMessage>
{
    public Task HandleAsync(EmptyMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
}

public class EmptyMessageHandler3 : IMessageHandler<EmptyMessage>
{
    public Task HandleAsync(EmptyMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
}
