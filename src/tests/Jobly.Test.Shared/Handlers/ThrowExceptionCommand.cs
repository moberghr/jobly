using Jobly.Core.Handlers;

namespace Jobly.Core.Handlers;

public class ThrowExceptionRequest : IJob
{
    public DateTime? SchedululeTime { get; set; }
}

public class ThrowExceptionCommand : IJobHandler<ThrowExceptionRequest>
{
    private readonly IPublisher _publisher;

    public ThrowExceptionCommand(IPublisher publisher)
    {
        _publisher = publisher;
    }

    public async Task HandleAsync(ThrowExceptionRequest message, CancellationToken ct)
    {
        throw new Exception("This is from ThrowExceptionCommand");
    }
}
