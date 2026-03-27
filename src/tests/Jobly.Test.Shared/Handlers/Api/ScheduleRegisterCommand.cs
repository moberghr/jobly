using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Jobly.Core.Helper;
using Jobly.Test.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace Jobly.Core.Handlers;

public class ScheduleRegisterRequest : IJob
{
    public string Email { get; set; }

    public DateTime ScheduleTime { get; set; }
}

public class ScheduleRegisterCommand : IJobHandler<ScheduleRegisterRequest>
{
    private readonly TestContext _context;
    private readonly IPublisher _publisher;

    public ScheduleRegisterCommand(TestContext context, IPublisher publisher)
    {
        _context = context;
        _publisher = publisher;
    }

    public async Task HandleAsync(ScheduleRegisterRequest message, CancellationToken ct)
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

        using var transaction = await _context.Database.BeginTransactionAsync();

        await _context.SaveChangesAsync();

        var sendEmailRequest = new SendEmailRequest
        {
            EmailLogId = emailLog.Id,
        };

        for (var i = 0; i < 10; i++)
        {
            // Original code, works fine with one parameter
            await _publisher.Schedule(sendEmailRequest, message.ScheduleTime);

            // JobData parameters
            var jobParams = new JobParameters
            {
                ScheduleTime = message.ScheduleTime,
                Priority = Priority.High
            };
            await _publisher.Enqueue(sendEmailRequest, jobParams);
        }

        await _context.SaveChangesAsync();

        await transaction.CommitAsync();
    }
}
