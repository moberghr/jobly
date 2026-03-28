using Jobly.Core.Handlers;
using Microsoft.EntityFrameworkCore;

namespace Jobly.Core.Handlers;

public class SendEmailCommand : IJobHandler<SendEmailRequest>
{
    private readonly TestContext _context;

    public SendEmailCommand(TestContext context)
    {
        _context = context;
    }

    public async Task HandleAsync(SendEmailRequest message, CancellationToken ct)
    {
        var emailLog = await _context.EmailLogs
            .Where(x => x.Id == message.EmailLogId)
            .FirstAsync(cancellationToken: ct);

        emailLog.ProcessedTime = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);
    }
}

public class SendEmailRequest : IJob
{
    public int EmailLogId { get; set; }
}
