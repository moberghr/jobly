using MediatR;

namespace Jobly.Core.Handlers;

public class FailedJobResponse
{

}

public class FailedJobRequest : IRequest<FailedJobResponse>
{
    public DateTime? SchedululeTime { get; set; }
}

public class FailedCommand : IRequestHandler<FailedJobRequest, FailedJobResponse>
{
    private readonly TestContext _context;
    private readonly IPublisher _publisher;

    public FailedCommand(TestContext context, IPublisher publisher)
    {
        _context = context;
        _publisher = publisher;
    }

    public async Task<FailedJobResponse> Handle(FailedJobRequest request, CancellationToken cancellationToken)
    {
        if (request.SchedululeTime.HasValue)
        {
            await _publisher.Publish(new ThrowExceptionRequest(), request.SchedululeTime.Value);

            await _context.SaveChangesAsync();

            return new();
        }

        for (var i = 0; i < 10; i++)
        {
            await _publisher.Publish(new ThrowExceptionRequest());
        }

        await _context.SaveChangesAsync();

        return new();
    }
}
