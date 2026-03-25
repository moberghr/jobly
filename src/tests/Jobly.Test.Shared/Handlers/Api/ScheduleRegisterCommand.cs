using Jobly.Core.Enums;
using Jobly.Core.Helper;
using Jobly.Test.Shared.Entities;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobly.Core.Handlers;

public class ScheduleRegisterResponse
{

}

public class ScheduleRegisterRequest : IRequest<ScheduleRegisterResponse>
{
    public string Email { get; set; }

    public DateTime ScheduleTime { get; set; }
}

public class ScheduleRegisterCommand : IRequestHandler<ScheduleRegisterRequest, ScheduleRegisterResponse>
{
    private readonly TestContext _context;
    private readonly IPublisher _publisher;

    public ScheduleRegisterCommand(TestContext context, IPublisher publisher)
    {
        _context = context;
        _publisher = publisher;
    }

    public async ValueTask<ScheduleRegisterResponse> Handle(ScheduleRegisterRequest request, CancellationToken cancellationToken)
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

        using var transaction = await _context.Database.BeginTransactionAsync();

        await _context.SaveChangesAsync();

        var sendEmailRequest = new SendEmailRequest
        {
            EmailLogId = emailLog.Id,
        };

        for (var i = 0; i < 10; i++)
        {
            // Original code, works fine with one parameter
            await _publisher.Publish(sendEmailRequest, request.ScheduleTime);

            // JobData parameters
            var jobParams = new JobParameters
            {
                ScheduleTime = request.ScheduleTime,
                Priority = Priority.High
            };
            await _publisher.Publish(sendEmailRequest, jobParams);
        }

        await _context.SaveChangesAsync();

        await transaction.CommitAsync();

        return new();

    }
}

