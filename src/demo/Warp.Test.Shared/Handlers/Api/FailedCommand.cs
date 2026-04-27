using Warp.Core.Handlers;

namespace Warp.Core.Handlers;

public class FailedJobRequest : IJob
{
    public DateTime? SchedululeTime { get; set; }
}

public class FailedCommand : IJobHandler<FailedJobRequest>
{
    private readonly TestContext _context;
    private readonly IPublisher _publisher;

    public FailedCommand(TestContext context, IPublisher publisher)
    {
        _context = context;
        _publisher = publisher;
    }

    public async Task HandleAsync(FailedJobRequest message, CancellationToken cancellationToken)
    {
        if (message.SchedululeTime.HasValue)
        {
            await _publisher.Schedule(new ThrowExceptionRequest(), message.SchedululeTime.Value);

            await _context.SaveChangesAsync(cancellationToken);

            return;
        }

        for (var i = 0; i < 10; i++)
        {
            await _publisher.Enqueue(new ThrowExceptionRequest());
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
