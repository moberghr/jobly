using Handfire.Test.Shared.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Handfire.Core.Handlers;

public class RegisterDelete : IRequestHandler<DeleteRequest, DeleteResponse>
{
    private readonly TestContext _context;
    private readonly IPublisher _publisher;
    public RegisterDelete(TestContext context, Core.IPublisher publisher)
    {
        _context = context;
        _publisher = publisher;
    }

    public async Task<DeleteResponse> Handle(DeleteRequest request, CancellationToken cancellationToken)
    {
        var registration = await _context.Registrations
            .Where(x => x.Id == request.Id)
            .SingleOrDefaultAsync();

        if (registration == null)
        {
            throw new ArgumentException("registration dont exists!");
        }
        _context.Registrations.Remove(registration);

        var emailLog = new EmailLog
        {
            Email = registration.Email,
            Body = "Test email",
            Subject = "Deleting registration"
        };

        _context.EmailLogs.Add(emailLog);
        using var transaction = await _context.Database.BeginTransactionAsync();
        await _context.SaveChangesAsync();

        var sendEmailRequest = new SendEmailRequest
        {
            EmailLogId = emailLog.Id
        };

        await _publisher.Publish(sendEmailRequest);
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return new();
    }
}
public class DeleteRequest : IRequest<DeleteResponse>
{
    public long Id { get; set; }
}
public class DeleteResponse
{ }

