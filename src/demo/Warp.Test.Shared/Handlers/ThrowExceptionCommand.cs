using Warp.Core.Handlers;

namespace Warp.Core.Handlers;

public class ThrowExceptionRequest : IJob
{
    public DateTime? SchedululeTime { get; set; }
}

public class ThrowExceptionCommand : IJobHandler<ThrowExceptionRequest>
{
    public Task HandleAsync(ThrowExceptionRequest message, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("This is from ThrowExceptionCommand");
    }
}
