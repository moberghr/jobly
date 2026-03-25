using Mediator;

namespace Jobly.Tests.TestData.Handlers;
public class UnitCommand : IRequestHandler<UnitRequest, Unit>
{
    public ValueTask<Unit> Handle(UnitRequest request, CancellationToken cancellationToken)
    {
        return new ValueTask<Unit>(Unit.Value);
    }
}

public class UnitRequest : IRequest<Unit>
{

}
