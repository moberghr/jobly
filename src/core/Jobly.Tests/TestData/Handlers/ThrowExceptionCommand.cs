using Mediator;

namespace Jobly.Tests.TestData.Handlers;
public class ThrowExceptionCommand : IRequestHandler<ThrowExceptionRequest, Unit>
{
    public ValueTask<Unit> Handle(ThrowExceptionRequest request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}

public class ThrowExceptionRequest : IRequest<Unit>
{

}
