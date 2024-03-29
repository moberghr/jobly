using Jobly.Test.Shared.Entities;
using MediatR;

namespace Jobly.Core.Handlers;

public class RegisterCommand : IRequestHandler<RegisterRequest, RegisterResponse>
{
    private readonly TestContext _context;
    private readonly IPublisher _publisher;

    public RegisterCommand(TestContext context, IPublisher publisher)
    {
        _context = context;
        _publisher = publisher;
    }

    public async Task<RegisterResponse> Handle(RegisterRequest request, CancellationToken cancellationToken)
    {
        var registration = new Registration
        {
            Email = request.Email
        };

        _context.Registrations.Add(registration);

        var emailLog = new EmailLog
        {
            Email = request.Email,
            Body = "Test email",
            Subject = "Test subject"
        };

        _context.EmailLogs.Add(emailLog);


        var sendEmailRequest = new SendEmailRequest
        {
            EmailLogId = emailLog.Id
        };

        for (var i = 0; i < 10; i++)
        {
            await _publisher.Publish(sendEmailRequest);
        }

        using var transaction = await _context.Database.BeginTransactionAsync();

        await _context.SaveChangesAsync();
        string parentId = await _publisher.Publish(sendEmailRequest);

        for (int i = 0; i < 4; i++)
        {
            await _publisher.Publish(sendEmailRequest, parentId);
        }

        await _context.SaveChangesAsync();

        await transaction.CommitAsync();

        return new();
    }
}

public class RegisterRequest : IRequest<RegisterResponse>
{
    public string Email { get; set; }
}

public class RegisterResponse
{

}
