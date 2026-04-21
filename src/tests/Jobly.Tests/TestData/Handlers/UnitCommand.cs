using Jobly.Core.Handlers;

namespace Jobly.Tests.TestData.Handlers;

public class UnitCommand : IJobHandler<UnitRequest>
{
    public async Task HandleAsync(UnitRequest message, CancellationToken cancellationToken)
    {
    }
}

public class UnitRequest : IJob;
