using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Jobly.Core.Handlers;

public class SendEmailCommand : IRequestHandler<SendEmailRequest, SendEmailResponse>
{
    private readonly TestContext _context;
    
    public SendEmailCommand(TestContext context)
    {
        _context = context;
    }

    public async Task<SendEmailResponse> Handle(SendEmailRequest request, CancellationToken cancellationToken)
    {
        await Task.Delay(500);
        return new();
        // var emailLog = await _context.EmailLogs
        //     .Where(x => x.Id == request.EmailLogId)
        //     .FirstAsync();
        //
        // emailLog.ProcessedTime = DateTime.UtcNow;
        //
        // await _context.SaveChangesAsync();
        // return new();
    }
}

public class SendEmailRequest : IRequest<SendEmailResponse>
{
    public int EmailLogId { get; set; }
}

public class SendEmailResponse
{

}
