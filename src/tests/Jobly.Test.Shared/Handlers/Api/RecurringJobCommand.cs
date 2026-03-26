using Jobly.Core.Handlers;
using Jobly.Test.Shared.Entities;

namespace Jobly.Core.Handlers;

public class RecurringJobRequest : IJob
{
    public string Email { get; set; }

    public string Name { get; set; }

    public string Cron { get; set; }
}


public class RecurringJobCommand : IJobHandler<RecurringJobRequest>
{
    private readonly TestContext _context;
    private readonly IRecurringJobPublisher _publisher;

    public RecurringJobCommand(TestContext context, IRecurringJobPublisher publisher)
    {
        _context = context;
        _publisher = publisher;
    }

    public async Task HandleAsync(RecurringJobRequest message, CancellationToken ct)
    {
        var registration = new Registration
        {
            Email = message.Email
        };

        _context.Registrations.Add(registration);

        var emailLog = new EmailLog
        {
            Email = message.Email,
            Body = "Test email",
            Subject = "Test subject"
        };

        _context.EmailLogs.Add(emailLog);

        await _context.SaveChangesAsync();

        var sendEmailRequest = new SendEmailRequest
        {
            EmailLogId = emailLog.Id,
        };

        await _publisher.AddOrUpdateRecurringJob(sendEmailRequest, message.Name, message.Cron);

        await _context.SaveChangesAsync();
    }
}
