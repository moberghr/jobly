using Handfire.Core;
using Handfire.Core.Handlers;
using Handfire.Test.Shared.Entities;
using Handfire.Tests.TestData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Handfire.Tests;

public class UnitTest1 : SqlServerTestBase 
{
    [Fact]
    public async Task Test1()
    {
        var serviceScope = new Mock<IServiceScope>();
        serviceScope.Setup(x => x.ServiceProvider.GetRequiredService<TestContext>())
            .Returns(CreateTContext<TestContext>());

        var serviceScopeFactory = new Mock<IServiceScopeFactory>();
        serviceScopeFactory
            .Setup(x => x.CreateScope())
            .Returns(serviceScope.Object);

        var _publisher = new Publisher<TestContext>(CreateTContext<TestContext>());
        var _context = CreateContext();

        var registration = new Registration
        {
            Email = "Test"
        };

        _context.Registrations.Add(registration);

        var emailLog = new EmailLog
        {
            Email = "Test",
            Body = "Test email",
            Subject = "Test subject"
        };

        _context.EmailLogs.Add(emailLog);

        using var transaction = await _context.Database.BeginTransactionAsync();

        await _context.SaveChangesAsync();

        var sendEmailRequest = new SendEmailRequest
        {
            EmailLogId = emailLog.Id
        };

        for (var i = 0; i < 1000; i++)
        {
            await _publisher.Publish(sendEmailRequest);
        }

        await _context.SaveChangesAsync();

        await transaction.CommitAsync();
    }
}