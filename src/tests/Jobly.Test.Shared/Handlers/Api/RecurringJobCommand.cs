using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Jobly.Test.Shared.Entities;
using MediatR;

namespace Jobly.Core.Handlers;

public class RecurringJobResponse
{
}

public class RecurringJobRequest : IRequest<RecurringJobResponse>
{
    public string Email { get; set; }

    public string Name { get; set; }

    public string Cron { get; set; }
}


public class RecurringJobCommand : IRequestHandler<RecurringJobRequest, RecurringJobResponse>
{
    private readonly TestContext _context;
    private readonly IRecurringJobPublisher _publisher;

    public RecurringJobCommand(TestContext context, IRecurringJobPublisher publisher)
    {
        _context = context;
        _publisher = publisher;
    }

    public async Task<RecurringJobResponse> Handle(RecurringJobRequest request, CancellationToken cancellationToken)
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

        await _context.SaveChangesAsync();
        
        var sendEmailRequest = new SendEmailRequest
        {
            EmailLogId = emailLog.Id,
        };

        await _publisher.AddOrUpdateRecurringJob(sendEmailRequest, request.Name, request.Cron);

        await _context.SaveChangesAsync();

        return new();
    }
}