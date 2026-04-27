using Microsoft.EntityFrameworkCore;
using Warp.Core.Handlers;

namespace Warp.Core.Handlers;

public class SendEmailCommand : IJobHandler<SendEmailRequest>
{
    private readonly TestContext _context;

    public SendEmailCommand(TestContext context)
    {
        _context = context;
    }

    public async Task HandleAsync(SendEmailRequest message, CancellationToken cancellationToken)
    {
        var emailLog = await _context.EmailLogs
            .Where(x => x.Id == message.EmailLogId)
            .FirstAsync(cancellationToken: cancellationToken);

        emailLog.ProcessedTime = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
    }
}

public class SendEmailRequest : IJob
{
    public int EmailLogId { get; set; }
}
