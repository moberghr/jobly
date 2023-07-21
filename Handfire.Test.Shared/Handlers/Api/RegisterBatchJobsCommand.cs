using Handfire.Test.Shared.Entities;
using MediatR;

namespace Handfire.Core.Handlers;

public class RegisterBatchJobsCommand : IRequestHandler<RegisterBatchJobsRequest, RegisterBatchJobsResponse>
{
    private readonly TestContext _context;
    private readonly IBatchPublisher _batchPublisher;

    public RegisterBatchJobsCommand(TestContext context, IBatchPublisher batchPublisher)
    {
        _context = context;
        _batchPublisher = batchPublisher;
    }

    public async Task<RegisterBatchJobsResponse> Handle(RegisterBatchJobsRequest request, CancellationToken cancellationToken)
    {
        var registration = new Registration
        {
            Email = request.Email,
        };

        _context.Registrations.Add(registration);

        var emailLog = new EmailLog
        {
            Email = registration.Email,
            Body = "Test email",
            Subject = "Test subject"
        };

        _context.EmailLogs.Add(emailLog);

        using var transaction = await _context.Database.BeginTransactionAsync();

        await _context.SaveChangesAsync();

        var sendEmailRequests = new List<SendEmailRequest>();

        for (int i = 0; i < 30; i++)
        {
            var sendEmailRequest = new SendEmailRequest
            {
                EmailLogId = emailLog.Id
            };
            sendEmailRequests.Add(sendEmailRequest);
        }

        var placeholderJobId = await _batchPublisher.StartNew(sendEmailRequests);

        await _context.SaveChangesAsync();

        await transaction.CommitAsync();

        return new();
    }
}

public class RegisterBatchJobsRequest : IRequest<RegisterBatchJobsResponse>
{
    public string Email { get; set; } = string.Empty;
}

public class RegisterBatchJobsResponse
{
}