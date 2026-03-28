using Jobly.Core.Handlers;
using Jobly.Test.Shared.Entities;

namespace Jobly.Core.Handlers;

public class RegisterCommand : IJobHandler<RegisterRequest>
{
    private readonly TestContext _context;
    private readonly IPublisher _publisher;
    private readonly IBatchPublisher _batchPublisher;

    public RegisterCommand(TestContext context, IPublisher publisher, IBatchPublisher batchPublisher)
    {
        _context = context;
        _publisher = publisher;
        _batchPublisher = batchPublisher;
    }

    public async Task HandleAsync(RegisterRequest message, CancellationToken ct)
    {
        var registration = new Registration
        {
            Email = message.Email,
        };

        _context.Registrations.Add(registration);

        var emailLog = new EmailLog
        {
            Email = message.Email,
            Body = "Test email",
            Subject = "Test subject",
        };

        _context.EmailLogs.Add(emailLog);

        await using var transaction = await _context.Database.BeginTransactionAsync(ct);

        await _context.SaveChangesAsync(ct);

        var sendEmailRequest = new SendEmailRequest
        {
            EmailLogId = emailLog.Id,
        };

        var batch = new List<SendEmailRequest>();

        for (var i = 0; i < 20; i++)
        {
            batch.Add(sendEmailRequest);
            await _publisher.Enqueue(sendEmailRequest);
        }

        await _batchPublisher.StartNew(batch);

        var parentId = await _publisher.Enqueue(sendEmailRequest);

        for (var i = 0; i < 4; i++)
        {
            await _publisher.Enqueue(sendEmailRequest, parentId);
        }

        await _context.SaveChangesAsync(ct);

        await transaction.CommitAsync(ct);
    }
}

public class RegisterRequest : IJob
{
    public string? Email { get; set; }
}
