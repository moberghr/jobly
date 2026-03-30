using Jobly.Core.Handlers;

namespace Jobly.Core.Handlers;

public class EmptyCommand : IJobHandler<EmptyRequest>
{
    public Task HandleAsync(EmptyRequest message, CancellationToken ct) => Task.CompletedTask;
}

public class EmptyRequest : IJob;
