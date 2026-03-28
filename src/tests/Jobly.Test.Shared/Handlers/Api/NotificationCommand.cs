using Jobly.Core.Handlers;
using Microsoft.Extensions.Logging;

namespace Jobly.Core.Handlers;

public class OrderNotification : IMessage;

public class OrderEmailHandler : IMessageHandler<OrderNotification>
{
    private readonly ILogger<OrderEmailHandler> _logger;

    public OrderEmailHandler(ILogger<OrderEmailHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(OrderNotification message, CancellationToken ct)
    {
        _logger.LogInformation("Sending order confirmation email");
        return Task.CompletedTask;
    }
}

public class OrderSlackHandler : IMessageHandler<OrderNotification>
{
    private readonly ILogger<OrderSlackHandler> _logger;

    public OrderSlackHandler(ILogger<OrderSlackHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(OrderNotification message, CancellationToken ct)
    {
        _logger.LogInformation("Posting order notification to Slack");
        return Task.CompletedTask;
    }
}
