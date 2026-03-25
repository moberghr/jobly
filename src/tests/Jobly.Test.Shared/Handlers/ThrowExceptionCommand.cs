using Mediator;

namespace Jobly.Core.Handlers;
public class ThrowExceptionResponse
{

}

public class ThrowExceptionRequest : IRequest<ThrowExceptionResponse>
{
    public DateTime? SchedululeTime { get; set; }
}

public class ThrowExceptionCommand : IRequestHandler<ThrowExceptionRequest, ThrowExceptionResponse>
{
    private readonly IPublisher _publisher;

    public ThrowExceptionCommand(IPublisher publisher)
    {
        _publisher = publisher;
    }

    public ValueTask<ThrowExceptionResponse> Handle(ThrowExceptionRequest request, CancellationToken cancellationToken)
    {
        throw new Exception("This is from ThrowExceptionCommand");
    }
}