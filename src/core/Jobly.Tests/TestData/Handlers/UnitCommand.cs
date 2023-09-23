using MediatR;

namespace Jobly.Tests.TestData.Handlers;
public class UnitCommand : IRequestHandler<UnitRequest, Unit>
{
    public Task<Unit> Handle(UnitRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(Unit.Value);
    }
}

public class UnitRequest : IRequest<Unit>
{

}
